"""Native feature resolution and offline reviewed-bundle CLI checks."""
from __future__ import annotations

import errno
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import keycloak_check as check  # noqa: E402


class BundleTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.bundle = Path(self.directory.name)
        (self.bundle / "VERSION").write_text(check.VERSION + "\n")
        for name in ("stage", "prod"):
            shutil.copyfile(ROOT / f"compose/keycloak/{name}.keycloak.conf", self.bundle / f"{name}.keycloak.conf")
            shutil.copyfile(ROOT / f"compose/keycloak/{name}.realm.json", self.bundle / f"{name}.realm.json")
        self.policy = check.read_json(check.POLICY_PATH)
        self.environment = {key: value for key, value in os.environ.items()
                            if not key.startswith("KC_FEATURE")}

    def config(self):
        return check.configuration(self.bundle / "stage.keycloak.conf")

    def cli(self, *args, env=None):
        return subprocess.run([sys.executable, str(ROOT / "scripts/keycloak_check.py"),
                               "--bundle", str(self.bundle), *args], env=self.environment | (env or {}),
                              capture_output=True, text=True, timeout=5)

    def test_native_safe_stage_and_prod_profiles_pass(self):
        selected = check.validate_bundle(self.bundle, {})
        self.assertEqual({"stage", "prod"}, set(selected))
        for value in selected.values():
            self.assertEqual("v1", value["organization"])
            self.assertEqual("v2", value["token-exchange-standard"])
            self.assertNotIn("token-exchange", value)
            self.assertNotIn("token-exchange-delegation", value)
        result = self.cli()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("stage/prod", result.stdout)

    def test_planted_native_preview_flag_names_the_feature(self):
        path = self.bundle / "stage.keycloak.conf"
        source = path.read_text().replace("features=", "features=token-exchange-delegation,")
        # Remove its disable entry so the effective native flag really enables it.
        source = source.replace(",token-exchange-delegation", "")
        path.write_text(source)
        result = self.cli()
        self.assertEqual(1, result.returncode)
        self.assertIn("token-exchange-delegation", result.stderr)

    def test_invented_realm_server_flags_fail_but_user_attributes_are_not_flags(self):
        path = self.bundle / "stage.realm.json"
        for field in ("features", "KC_FEATURES", "features-disabled", "KC_FEATURE_TOKEN_EXCHANGE_DELEGATION"):
            with self.subTest(field=field):
                path.write_text(json.dumps({"realm": "stage", field: "token-exchange-delegation"}))
                result = self.cli()
                self.assertEqual(1, result.returncode)
                self.assertIn(field, result.stderr)
        realm = json.loads((ROOT / "compose/keycloak/stage.realm.json").read_text())
        realm["users"] = [{"attributes": {"features": ["business"]}}]
        path.write_text(json.dumps(realm))
        self.assertEqual(0, self.cli().returncode)

    def test_preview_unknown_and_deprecated_versions_are_rejected(self):
        for option in ("token-exchange-delegation", "token-exchange-standard:v999", "unknown-new-feature",
                       "preview", "login:v1", "account:v1"):
            with self.subTest(option=option):
                result = self.cli("--features", option, "--features-disabled", "token-exchange,twitter-broker")
                self.assertEqual(1, result.returncode, result.stdout)
                self.assertIn(option.split(":")[0], result.stderr)

    def test_environment_list_replaces_configuration_list(self):
        result = self.cli(env={"KC_FEATURES": "token-exchange-delegation", "KC_FEATURES_DISABLED": "token-exchange"})
        self.assertEqual(1, result.returncode)
        self.assertIn("token-exchange-delegation", result.stderr)

    def test_single_environment_flag_cannot_bypass_disabled_list(self):
        for name, setting in (("TOKEN_EXCHANGE_DELEGATION", "enabled"), ("LOGIN", "v1"),
                              ("ORGANIZATION", "disabled"), ("TOKEN_EXCHANGE_STANDARD", "disabled")):
            with self.subTest(name=name):
                result = self.cli(env={"KC_FEATURE_" + name: setting})
                self.assertEqual(1, result.returncode)
                self.assertIn(name.lower().replace("_", "-"), result.stderr)

    def test_single_native_feature_overrides_both_lists(self):
        config = self.config() | {"features": "organization", "features-disabled": "organization,token-exchange,twitter-broker"}
        result = check.effective_features(config, {"KC_FEATURE_ORGANIZATION": "v1"}, self.policy)
        self.assertEqual("v1", result["organization"])

    def test_native_feature_dependencies_must_remain_enabled_at_the_required_version(self):
        for name, item in self.policy["features"].items():
            for version, dependencies in item.get("dependencies", {}).items():
                for dependency in dependencies:
                    with self.subTest(feature=name, version=version, dependency=dependency):
                        required = dependency.split(":")[0]
                        environment = {"KC_FEATURE_" + name.upper().replace("-", "_"): version,
                                       "KC_FEATURE_" + required.upper().replace("-", "_"): "disabled"}
                        with self.assertRaisesRegex(check.CheckError, "requires enabled feature " + required):
                            check.effective_features(self.config(), environment, self.policy)
        result = self.cli(env={"KC_FEATURE_AUTHORIZATION": "disabled"})
        self.assertEqual(1, result.returncode)
        self.assertIn("authorization:v1", result.stderr)

    def test_unknown_individual_and_bad_versions_fail_closed(self):
        for name, setting in (("NEW_FLAG", "disabled"), ("ORGANIZATION", "v999"), ("ORGANIZATION", "true")):
            with self.subTest(name=name, setting=setting):
                result = self.cli(env={"KC_FEATURE_" + name: setting})
                self.assertEqual(1, result.returncode)
                self.assertIn(name.lower().replace("_", "-"), result.stderr)

    def test_every_known_unsupported_version_is_refused_when_effectively_enabled(self):
        for name, item in self.policy["features"].items():
            for version, status in item["versions"].items():
                if status == "supported":
                    continue
                with self.subTest(name=name, version=version):
                    key = "KC_FEATURE_" + name.upper().replace("-", "_")
                    with self.assertRaisesRegex(check.CheckError, "Unsupported feature " + name):
                        check.effective_features(self.config(), {key: version}, self.policy)

    def test_known_supported_explicit_versions_are_accepted(self):
        for name, item in self.policy["features"].items():
            for version, status in item["versions"].items():
                if status != "supported":
                    continue
                with self.subTest(name=name, version=version):
                    key = "KC_FEATURE_" + name.upper().replace("-", "_")
                    self.assertEqual(version, check.effective_features(self.config(), {key: version}, self.policy)[name])

    def test_native_version_resolution_prefers_default_before_disabled_default(self):
        config = self.config() | {"features": "organization,token-exchange-standard,identity-brokering-api"}
        config["features-disabled"] = "token-exchange,twitter-broker"
        result = check.effective_features(config, {}, self.policy)
        self.assertEqual("v1", result["identity-brokering-api"])
        config["features"] = "organization,token-exchange-standard,identity-brokering-api:v2"
        self.assertEqual("v2", check.effective_features(config, {}, self.policy)["identity-brokering-api"])

    def test_cli_overrides_environment_lists(self):
        result = self.cli("--features", "organization:v1,token-exchange-standard:v2",
                          env={"KC_FEATURES": "token-exchange-delegation"})
        self.assertEqual(0, result.returncode, result.stderr)

    def test_invalid_and_conflicting_native_options_fail(self):
        for options in (("organization,organization:v1", "token-exchange,twitter-broker"),
                        ("organization", "organization,token-exchange,twitter-broker"),
                        ("organization", "organization:v1"),
                        ("organization", "hostname"), ("organization", "rolling-updates")):
            with self.subTest(options=options):
                result = self.cli("--features", options[0], "--features-disabled", options[1])
                self.assertEqual(1, result.returncode)

    def test_config_single_flags_and_native_substitution(self):
        config = self.config() | {"feature-organization": "${ORG_VERSION:v1}"}
        self.assertEqual("v1", check.effective_features(config, {}, self.policy)["organization"])
        with self.assertRaisesRegex(check.CheckError, "Required feature organization"):
            check.effective_features(config, {"ORG_VERSION": "disabled"}, self.policy)
        config["feature-organization"] = "disabled"
        self.assertEqual("v1", check.effective_features(config, {"KC_FEATURE_ORGANIZATION": "enabled"}, self.policy)["organization"])

    def test_bundle_exact_pin_and_missing_files_fail(self):
        (self.bundle / "VERSION").write_text("26.8.1\n")
        self.assertIn("26.8.0", self.cli().stderr)
        (self.bundle / "VERSION").unlink()
        self.assertEqual(1, self.cli().returncode)

    def test_credentials_in_realm_and_other_config_never_printed(self):
        secret = "credential-not-for-terminal-output"
        realm = json.loads((ROOT / "compose/keycloak/stage.realm.json").read_text())
        realm["clients"][1]["secret"] = secret
        (self.bundle / "stage.realm.json").write_text(json.dumps(realm))
        path = self.bundle / "stage.keycloak.conf"
        path.write_text(path.read_text().replace("db-password=${MAF_IDP_DB_PASSWORD}", "db-password=" + secret))
        result = self.cli(env={"KC_DB_PASSWORD": secret})
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn(secret, result.stdout + result.stderr)
        (self.bundle / "stage.realm.json").write_text('{"secret":"' + secret)
        result = self.cli()
        self.assertEqual(1, result.returncode)
        self.assertNotIn(secret, result.stdout + result.stderr)

    @unittest.skipUnless(hasattr(os, "mkfifo"), "Requires POSIX FIFO for deterministic CLI signal coverage")
    def test_cli_sigint_and_sigterm_exit_130_while_reading_bundle(self):
        version = self.bundle / "VERSION"
        version.unlink()
        os.mkfifo(version)
        for signum in (signal.SIGINT, signal.SIGTERM):
            with self.subTest(signum=signum):
                process = subprocess.Popen([sys.executable, str(ROOT / "scripts/keycloak_check.py"),
                                            "--bundle", str(self.bundle)], env=self.environment,
                                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                writer = None
                try:
                    deadline = time.monotonic() + 5
                    while writer is None and time.monotonic() < deadline:
                        try:
                            writer = os.open(version, os.O_WRONLY | os.O_NONBLOCK)
                        except OSError as error:
                            if error.errno != errno.ENXIO:
                                raise
                            time.sleep(0.01)
                    self.assertIsNotNone(writer, "CLI did not begin reading VERSION")
                    process.send_signal(signum)
                    # Release EOF after the signal. Some POSIX implementations restart a FIFO open/read
                    # before Python dispatches its pending handler; keeping the writer open can deadlock
                    # this artificial read even though the signal is pending. Exit 130 still proves the
                    # CLI handled it rather than completing validation (or taking SIGTERM's default exit).
                    os.close(writer)
                    writer = None
                    stdout, stderr = process.communicate(timeout=5)
                    self.assertEqual(130, process.returncode, stdout + stderr)
                    self.assertIn("stopped", stderr)
                    self.assertNotIn("supported server features only", stdout)
                finally:
                    if writer is not None:
                        os.close(writer)
                    if process.poll() is None:
                        process.kill()
                        process.communicate()


if __name__ == "__main__":
    unittest.main()
