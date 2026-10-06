"""The shared compose env files hold no secret (introduce-plugins 5.2): every secret-named key is a reference to the
host environment (${VAR:-default}), never a literal. The one literal is CI's stub placeholder, declared not a secret."""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENV_DIR = ROOT / "compose" / "env"
SECRET = re.compile(r"(^JEV_MAF_LAB$|key|token|secret|password)", re.IGNORECASE)
REFERENCE = re.compile(r"^\$\{[A-Z][A-Z0-9_]*:-[^}]*\}$")
NOT_SECRETS = {("ci-models.env", "JEV_MAF_LAB", "ci-stub-placeholder")}


def entries(path: Path) -> list[tuple[str, str]]:
    found = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip() and not line.lstrip().startswith("#"):
            key, _, value = line.partition("=")
            found.append((key.strip(), value.strip()))
    return found


class ComposeEnvTests(unittest.TestCase):
    def test_every_secret_named_key_comes_from_the_host_environment(self):
        # The core's env files and each plugin's own (plugins/<name>/files/*.env), held to the same rule.
        files = sorted(ENV_DIR.glob("*.env")) + sorted(ROOT.glob("plugins/*/files/*.env"))
        self.assertTrue(files, "no compose/env/*.env")
        literals = [f"{f.name}: {k}={v}" for f in files for k, v in entries(f)
                    if SECRET.search(k) and not REFERENCE.match(v) and (f.name, k, v) not in NOT_SECRETS]
        self.assertEqual([], literals)

    def test_the_check_catches_a_planted_literal(self):
        self.assertTrue(SECRET.search("Compliance__ClientSecret"))
        self.assertFalse(REFERENCE.match("assistant-dev-secret"))
        self.assertTrue(REFERENCE.match("${COMPLIANCE_CLIENT_SECRET:-assistant-dev-secret}"))


if __name__ == "__main__":
    unittest.main()
