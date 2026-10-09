"""Local HTTP fixtures for terminal allowance writes, bootstrap, and safe stopping."""
from __future__ import annotations

import importlib.util
import io
import json
import os
import signal
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import tenant_plugins_bootstrap as bootstrap  # noqa: E402

spec = importlib.util.spec_from_file_location("tenant_allow", ROOT / "plugins/platform-admin/files/tenant_allow.py")
tenant_allow = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tenant_allow)


def installed(*manifests, env="dev"):
    return {"env": env, "plugins": [{"manifest": p} for p in manifests]}


def manifest(name, scope="tenant", **kwargs):
    return {"name": name, "scope": scope, **kwargs}


class Terminal(io.StringIO):
    def isatty(self):
        return True


def advancing_clock(values):
    values = iter(values)
    last = 0

    def now():
        nonlocal last
        last = next(values, last)
        return last
    return now


class BootstrapTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        self.rows = {}
        self.failure = None
        self.on_put = lambda: None
        self.base_url = "http://fixture.invalid"
        transport = patch.object(bootstrap.urllib.request, "urlopen", self.respond)
        transport.start()
        self.addCleanup(transport.stop)
        self.document = installed(manifest("dev-login", "installation"), manifest("billing"),
                                  manifest("private", private_to="firm-b"), manifest("monitor", "installation"))

    def respond(self, request, timeout):
        self.assertEqual(10, timeout)
        self.assertEqual("application/json", request.get_header("Content-type"))
        path = request.full_url.removeprefix(self.base_url)
        body = json.loads(request.data)
        self.calls.append((request.get_method(), path, body, request.get_header("Authorization")))
        if self.failure:
            raise urllib.error.HTTPError(request.full_url, self.failure, "secret-firm-a must not leak", {},
                                         io.BytesIO(b'{"error":"secret-firm-a must not leak"}'))
        if path == "/dev/token":
            self.assertEqual("POST", request.get_method())
            self.assertEqual({"persona", "tenantId", "audience"}, set(body))
            self.assertEqual("operator", body["persona"])
            self.assertEqual("api", body["audience"])
            reply = {"token": "secret-" + body["tenantId"]}
        else:
            self.assertEqual("PUT", request.get_method())
            self.assertEqual({"allowed", "enabled"}, set(body))
            token = request.get_header("Authorization").removeprefix("Bearer secret-")
            self.rows[token, path] = body
            self.on_put()
            reply = {"plugin": path.rsplit("/", 1)[1], **body}
        return io.BytesIO(json.dumps(reply).encode())

    def run_bootstrap(self, **kwargs):
        output = kwargs.pop("output", io.StringIO())
        status = bootstrap.bootstrap(self.document, self.base_url, stop=kwargs.pop("stop", bootstrap.Stop()),
                                     output=output, **kwargs)
        return status, output.getvalue()

    def test_installed_tenant_only_private_owner_and_idempotent_requests(self):
        status, output = self.run_bootstrap()
        self.assertEqual(0, status)
        self.assertEqual("firm-a: 1 plugins allowed and enabled\nfirm-b: 2 plugins allowed and enabled\n"
                         "firm-c: 1 plugins allowed and enabled\n", output)
        self.assertEqual(4, len(self.rows))
        self.assertEqual({"firm-a", "firm-b", "firm-c"}, {tenant for tenant, _ in self.rows})
        self.assertNotIn(("firm-a", "/api/platform/plugins/private"), self.rows)
        self.assertTrue(all(row == {"allowed": True, "enabled": True} for row in self.rows.values()))
        before = dict(self.rows)
        self.run_bootstrap()
        self.assertEqual(before, self.rows)
        self.assertNotIn("secret-", output)

    def test_no_requests_without_dev_login_or_outside_dev_ci(self):
        self.document = installed(manifest("billing"))
        self.assertEqual((0, ""), self.run_bootstrap())
        self.document = installed(manifest("dev-login", "installation"), manifest("billing"), env="qa")
        self.assertEqual((0, ""), self.run_bootstrap())
        self.assertEqual([], self.calls)
        self.assertEqual(0, self.run_bootstrap(ci=True)[0])
        self.assertEqual(3, len(self.rows))

    def test_empty_tenant_plugin_set_never_obtains_tokens(self):
        self.document = installed(manifest("dev-login", "installation"))
        status, output = self.run_bootstrap()
        self.assertEqual(0, status)
        self.assertEqual([], self.calls)
        self.assertEqual(3, output.count("0 plugins allowed and enabled"))

    def test_terminal_progress_after_three_seconds_counts_only_writes(self):
        clock = advancing_clock((0, 1, 3.1, 4, 5))
        status, output = self.run_bootstrap(output=Terminal(), clock=clock)
        self.assertEqual(0, status)
        self.assertNotIn("1/4", output)
        self.assertIn("2/4", output)
        self.assertIn("4/4", output)
        self.assertEqual(3, output.count("plugins allowed and enabled"))

    def test_progress_is_shown_while_first_http_write_is_still_running(self):
        shown = threading.Event()
        clock = [0]

        class WatchingTerminal(Terminal):
            def write(self, text):
                result = super().write(text)
                if "0/4" in text:
                    shown.set()
                return result

        def held_write():
            clock[0] = 3.1
            self.assertTrue(shown.wait(2), "no progress appeared during the active call")

        self.on_put = held_write
        status, output = self.run_bootstrap(output=WatchingTerminal(), clock=lambda: clock[0])
        self.assertEqual(0, status)
        self.assertIn("0/4", output)
        self.assertIn("4/4", output)

    def test_redirected_output_has_only_tenant_lines_even_when_slow(self):
        clock = advancing_clock((0, 4, 5, 6, 7))
        status, output = self.run_bootstrap(clock=clock)
        self.assertEqual(0, status)
        self.assertEqual(3, len(output.splitlines()))

    def test_failed_write_reports_completed_tenants_without_response_body(self):
        self.failure = 403
        output = io.StringIO()
        with self.assertRaisesRegex(bootstrap.RequestError, r"HTTP 403") as error:
            self.run_bootstrap(output=output)
        self.assertNotIn("secret", str(error.exception))
        self.assertIn("Completed tenants: none; rerun make up.", output.getvalue())

    def test_stop_after_token_does_not_start_a_write(self):
        stop = bootstrap.Stop()
        original = bootstrap.operator_token

        def interrupted(*args):
            token = original(*args)
            stop.requested = True
            return token

        with patch.object(bootstrap, "operator_token", interrupted):
            status, output = self.run_bootstrap(stop=stop)
        self.assertEqual(130, status)
        self.assertEqual(1, len(self.calls))
        self.assertIn("Stopped after 0/4", output)

    def test_stop_between_writes_reports_completed_tenants(self):
        stop = bootstrap.Stop()
        self.on_put = lambda: setattr(stop, "requested", len(self.rows) == 2)
        status, output = self.run_bootstrap(stop=stop)
        self.assertEqual(130, status)
        self.assertEqual(2, len(self.rows))
        self.assertIn("Completed tenants: firm-a; rerun make up.", output)
        self.assertNotIn("firm-b: 2", output)

    def test_sigint_and_sigterm_finish_active_write_and_exit_130(self):
        for signum in (signal.SIGINT, signal.SIGTERM):
            with self.subTest(signal=signum), tempfile.TemporaryDirectory() as tmp:
                path = Path(tmp) / "installed.json"
                path.write_text(json.dumps(self.document))
                entered, release = Path(tmp) / "entered", Path(tmp) / "release"
                calls, committed = Path(tmp) / "calls", Path(tmp) / "committed"
                fixture = '''
import io, json, runpy, sys, time, urllib.request
from pathlib import Path
root = Path(sys.argv[1])
script = sys.argv[2]
def respond(request, timeout):
    with (root / "calls").open("a") as calls:
        calls.write(request.get_method() + "\\n")
    body = json.loads(request.data)
    if request.get_method() == "POST":
        reply = {"token": "secret-" + body["tenantId"]}
    else:
        (root / "entered").touch()
        deadline = time.monotonic() + 5
        while not (root / "release").exists() and time.monotonic() < deadline:
            time.sleep(0.01)
        (root / "committed").write_text(json.dumps(body))
        reply = {"plugin": "billing", **body}
    return io.BytesIO(json.dumps(reply).encode())
urllib.request.urlopen = respond
sys.argv = [script, "--installed", str(root / "installed.json")]
runpy.run_path(script, run_name="__main__")
'''
                process = subprocess.Popen([sys.executable, "-c", fixture, tmp,
                                            str(ROOT / "scripts/tenant_plugins_bootstrap.py")],
                                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                try:
                    deadline = time.monotonic() + 5
                    while not entered.exists() and time.monotonic() < deadline and process.poll() is None:
                        time.sleep(0.01)
                    self.assertTrue(entered.exists(), "bootstrap did not reach the write")
                    process.send_signal(signum)
                    self.assertIsNone(process.poll())
                    release.touch()
                    output, errors = process.communicate(timeout=5)
                    self.assertEqual(130, process.returncode, errors)
                    self.assertEqual({"allowed": True, "enabled": True}, json.loads(committed.read_text()))
                    self.assertEqual("POST\nPUT\n", calls.read_text())
                    self.assertIn("Completed tenants: firm-a; rerun make up.", output)
                    self.assertNotIn("secret-", output + errors)
                finally:
                    release.touch()
                    if process.poll() is None:
                        process.kill()
                        process.communicate()

    def test_interrupt_followed_by_transport_failure_still_exits_130(self):
        stop = bootstrap.Stop()

        def interrupted(*args, **kwargs):
            stop.requested = True
            raise bootstrap.RequestError("transport failed")

        with patch.object(bootstrap, "operator_token", side_effect=interrupted):
            output = io.StringIO()
            status = bootstrap.bootstrap(installed(manifest("dev-login", scope="installation"), manifest("billing")),
                                         self.base_url, stop=stop, output=output)
        self.assertEqual(130, status)
        self.assertIn("rerun make up", output.getvalue())
        self.assertEqual([], self.calls)

        stop.requested = False
        with patch.object(tenant_allow, "Stop", return_value=stop), patch.object(tenant_allow, "operator_token", side_effect=interrupted), patch.dict(os.environ, {"MAF_BEARER_TOKEN": ""}), redirect_stderr(io.StringIO()) as errors:
            status = tenant_allow.main(["--base-url", self.base_url, "--tenant", "firm-a", "--plugin", "billing"])
        self.assertEqual(130, status)
        self.assertIn("outcome is unknown", errors.getvalue())
        self.assertEqual([], self.calls)

    def test_tenant_allow_dev_token_default_and_enable_option(self):
        for enable in ("0", "1"):
            with self.subTest(enable=enable), patch.dict(os.environ, {"MAF_BEARER_TOKEN": ""}), redirect_stdout(io.StringIO()) as out:
                status = tenant_allow.main(["--base-url", self.base_url, "--tenant", "firm-b", "--plugin", "billing",
                                           "--enable", enable])
                self.assertEqual(0, status)
                self.assertEqual({"allowed": True, "enabled": enable == "1"}, self.rows["firm-b", "/api/platform/plugins/billing"])
                self.assertNotIn("secret-", out.getvalue())

    def test_supplied_operator_token_skips_dev_issuer_and_never_chooses_tenant(self):
        with patch.dict(os.environ, {"MAF_BEARER_TOKEN": "secret-firm-c"}), redirect_stdout(io.StringIO()) as out:
            status = tenant_allow.main(["--base-url", self.base_url, "--tenant", "firm-a", "--plugin", "billing"])
        self.assertEqual(0, status)
        self.assertEqual(1, len(self.calls))
        self.assertIn(("firm-c", "/api/platform/plugins/billing"), self.rows)
        self.assertIn("operator organization", out.getvalue())
        self.assertNotIn("firm-a", out.getvalue())

    def test_tenant_allow_http_errors_redact_response_and_token(self):
        self.failure = 403
        with patch.dict(os.environ, {"MAF_BEARER_TOKEN": "secret-firm-a"}), redirect_stderr(io.StringIO()) as error:
            status = tenant_allow.main(["--base-url", self.base_url, "--tenant", "firm-a", "--plugin", "billing"])
        self.assertEqual(1, status)
        self.assertIn("HTTP 403", error.getvalue())
        self.assertNotIn("secret", error.getvalue())

    def test_shared_and_path_injection_fail_before_http(self):
        for tenant, name in (("shared", "billing"), ("firm-a", "../billing")):
            with self.subTest(tenant=tenant, plugin=name), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
                tenant_allow.main(["--base-url", self.base_url, "--tenant", tenant, "--plugin", name])
            self.assertEqual(2, error.exception.code)
        self.assertEqual([], self.calls)


if __name__ == "__main__":
    unittest.main()
