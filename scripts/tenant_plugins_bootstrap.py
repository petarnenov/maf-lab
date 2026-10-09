#!/usr/bin/env python3
"""Allow and enable installed tenant plugins in the dev/CI fixture organizations.

All writes use an organization-scoped operator bearer; no tenant selector is sent
to the permissions API. Interrupts finish the current HTTP call before stopping.
"""
from __future__ import annotations

import argparse
import json
import os
import signal
import sys
import threading
import time
import urllib.error
import urllib.request
from contextlib import contextmanager
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FIXTURE_TENANTS = ("firm-a", "firm-b", "firm-c")


class RequestError(Exception):
    """A safe diagnostic, never an HTTP response body or a bearer token."""


class Stop:
    def __init__(self):
        self.requested = False

    def request(self, _signum, _frame):
        self.requested = True


@contextmanager
def stopping(stop):
    previous = {s: signal.signal(s, stop.request) for s in (signal.SIGINT, signal.SIGTERM)}
    try:
        yield
    finally:
        for s, handler in previous.items():
            signal.signal(s, handler)


def request_json(base_url, path, body, *, token=None, method="POST"):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request(base_url.rstrip("/") + path,
                                     json.dumps(body).encode(), headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        code = error.code
        error.close()
        raise RequestError(f"{method} {path} failed (HTTP {code}).") from None
    except (urllib.error.URLError, TimeoutError, OSError, ValueError):
        raise RequestError(f"{method} {path} failed; check the stack and operator credentials.") from None


def operator_token(base_url, tenant):
    response = request_json(base_url, "/dev/token",
                            {"persona": "operator", "tenantId": tenant, "audience": "api"})
    if not isinstance(response, dict) or not isinstance(response.get("token"), str) or not response["token"]:
        raise RequestError("The development issuer returned no operator token.")
    return response["token"]


def allow_plugin(base_url, plugin, token, *, enable=False):
    request_json(base_url, "/api/platform/plugins/" + plugin,
                 {"allowed": True, "enabled": enable}, token=token, method="PUT")


class Progress:
    def __init__(self, total, output, clock=time.monotonic):
        self.total, self.output, self.clock = total, output, clock
        self.started = clock()
        self.visible = False
        self.done = 0
        self.lock = threading.RLock()
        self.stopped = threading.Event()
        self.worker = None

    def start(self):
        if self.output.isatty():
            self.worker = threading.Thread(target=self.refresh, daemon=True)
            self.worker.start()

    def refresh(self):
        # A slow HTTP call must not postpone the first progress display until it returns.
        while not self.stopped.wait(0.2):
            self.update()

    def update(self, done=None):
        with self.lock:
            if done is not None:
                self.done = done
            if self.output.isatty() and self.clock() - self.started >= 3:
                self.visible = True
                width = 20
                filled = width * self.done // max(self.total, 1)
                print(f"\rTenant plugins [{'#' * filled}{'-' * (width - filled)}] {self.done}/{self.total}",
                      end="", file=self.output, flush=True)

    def clear(self):
        with self.lock:
            if self.visible:
                print(file=self.output, flush=True)
                self.visible = False

    def message(self, line):
        with self.lock:
            self.clear()
            print(line, file=self.output, flush=True)

    def finish(self):
        self.stopped.set()
        if self.worker:
            self.worker.join()
        self.clear()


def bootstrap(document, base_url, *, stop, output=sys.stdout, clock=time.monotonic, ci=False):
    manifests = [p["manifest"] for p in document["plugins"]]
    if not (document.get("env") == "dev" or ci) or not any(p["name"] == "dev-login" for p in manifests):
        return 0
    selected = {tenant: [p["name"] for p in manifests
                         if p.get("scope") == "tenant" and p.get("private_to", tenant) == tenant]
                for tenant in FIXTURE_TENANTS}
    progress = Progress(sum(map(len, selected.values())), output, clock)
    done = 0
    completed = []
    progress.start()
    try:
        for tenant, names in selected.items():
            if stop.requested:
                break
            token = operator_token(base_url, tenant) if names else None
            count = 0
            for name in names:
                if stop.requested:
                    break
                allow_plugin(base_url, name, token, enable=True)
                count += 1
                done += 1
                progress.update(done)
            if count == len(names):
                progress.message(f"{tenant}: {count} plugins allowed and enabled")
                completed.append(tenant)
            if stop.requested:
                break
    except RequestError:
        progress.message(f"Completed tenants: {', '.join(completed) or 'none'}; rerun make up.")
        if not stop.requested:
            raise
    finally:
        progress.finish()
    if stop.requested:
        print(f"Stopped after {done}/{progress.total} plugins. Completed tenants: "
              f"{', '.join(completed) or 'none'}; rerun make up.", file=output, flush=True)
        return 130
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default=os.environ.get("BASE_URL", "http://localhost:7171"))
    parser.add_argument("--installed", type=Path,
                        default=Path(os.environ.get("MAF_PLUGINS_ROOT", str(ROOT / "plugins"))) / ".installed")
    args = parser.parse_args(argv)
    stop = Stop()
    try:
        document = json.loads(args.installed.read_text(encoding="utf-8"))
        with stopping(stop):
            return bootstrap(document, args.base_url, stop=stop, ci=os.environ.get("CI_MODE") == "1")
    except (RequestError, OSError, ValueError, KeyError, TypeError) as error:
        # Do not print malformed input or raw transport exceptions containing credentials.
        print(str(error) if isinstance(error, RequestError) else "Could not read the installed plugin set.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
