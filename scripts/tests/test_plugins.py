"""Unit tests for scripts/plugins.py (introduce-plugins tasks 2.3, 3.6, 3.8). Run: python3 -m unittest discover -s scripts/tests"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import plugins  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]


def manifest(name: str, *, kind: str = "app", environments=("dev", "qa"), depends=(), extra: str = "") -> str:
    return (f'schema = 1\nname = "{name}"\nkind = "{kind}"\nscope = "installation"\n'
            f'environments = {json.dumps(list(environments))}\ndescription = "{name} plugin"\n'
            f'depends = {json.dumps(list(depends))}\nprogress = "None — a fixture"\nstopping = "None — a fixture"\n{extra}')


class PluginsTestCase(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.addCleanup(self._dir.cleanup)
        self.root = Path(self._dir.name) / "plugins"
        self.lb = Path(self._dir.name) / "lb"
        self.root.mkdir()
        self.lb.mkdir()
        (self.lb / "api.upstream.conf").write_text("upstream api_pool { server api:8080; }\n")
        self.env = {k: os.environ.get(k) for k in ("MAF_PLUGINS_ROOT", "MAF_LB_ROOT", "MAF_PLUGINS", "MAF_ENV")}
        os.environ["MAF_PLUGINS_ROOT"] = str(self.root)
        os.environ["MAF_LB_ROOT"] = str(self.lb)
        os.environ.pop("MAF_PLUGINS", None)
        os.environ.pop("MAF_ENV", None)
        self.addCleanup(self.restore)

    def restore(self):
        for k, v in self.env.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v

    def add(self, name: str, text: str | None = None, **files: str) -> Path:
        folder = self.root / name
        folder.mkdir()
        (folder / "plugin.toml").write_text(text if text is not None else manifest(name), encoding="utf-8")
        for rel, content in files.items():
            path = folder / rel.replace("__", "/")
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        return folder

    def resolved(self, **env: str) -> list[str]:
        os.environ.update(env)
        return [p.name for p in plugins.resolve()]


class ResolveTests(PluginsTestCase):
    def test_unset_installs_every_plugin_the_environment_allows_except_the_example(self):
        self.add("a")
        self.add("_example")
        self.add("prod-only", manifest("prod-only", environments=["prod"]))
        self.assertEqual(["a"], self.resolved())

    def test_none_installs_nothing(self):
        self.add("a")
        self.assertEqual([], self.resolved(MAF_PLUGINS="none"))

    def test_dependencies_come_first(self):
        self.add("base")
        self.add("top", manifest("top", depends=["base"]))
        self.assertEqual(["base", "top"], self.resolved(MAF_PLUGINS="top"))

    def test_a_missing_plugin_is_named(self):
        self.add("top", manifest("top", depends=["gone"]))
        with self.assertRaisesRegex(plugins.PluginError, r"plugin 'gone' does not exist \(needed by top\)"):
            self.resolved(MAF_PLUGINS="top")

    def test_a_cycle_is_named(self):
        self.add("a", manifest("a", depends=["b"]))
        self.add("b", manifest("b", depends=["a"]))
        with self.assertRaisesRegex(plugins.PluginError, "cycle: a → b → a"):
            self.resolved(MAF_PLUGINS="a")

    def test_an_environment_the_plugin_does_not_allow_is_refused_naming_it(self):
        self.add("monitor", manifest("monitor", environments=["dev", "qa"]))
        with self.assertRaisesRegex(plugins.PluginError, "plugin 'monitor' is not allowed in MAF_ENV=prod"):
            self.resolved(MAF_PLUGINS="monitor", MAF_ENV="prod")

    def test_an_invalid_manifest_is_refused(self):
        self.add("bad", manifest("bad").replace('stopping = "None — a fixture"\n', ""))
        with self.assertRaisesRegex(plugins.PluginError, "invalid manifest: /: missing required `stopping`"):
            self.resolved(MAF_PLUGINS="bad")


class ManifestTests(PluginsTestCase):
    def test_the_schema_and_the_forms_are_checked(self):
        self.add("x", manifest("x").replace('progress = "None — a fixture"', 'progress = "soon"') + 'colour = "blue"\n')
        problems = plugins.discover()["x"].problems
        self.assertTrue(any("unknown key `colour`" in p for p in problems), problems)
        self.assertTrue(any(p.startswith("/progress:") for p in problems), problems)

    def test_every_keyword_the_schema_uses_is_supported(self):
        # The validator implements a subset of JSON Schema: adding a keyword it lacks must fail here, not in make.
        schema = plugins.load_schema()
        self.assertEqual(schema.get("$schema"), "https://json-schema.org/draft/2020-12/schema")
        used = set()

        def walk(node):
            if isinstance(node, dict):
                used.update(node.keys())
                for key in ("properties",):
                    for sub in node.get(key, {}).values():
                        walk(sub)
                for key in ("items", "additionalProperties"):
                    if isinstance(node.get(key), dict):
                        walk(node[key])
        walk(schema)
        self.assertEqual(sorted(used - plugins.KNOWN_KEYWORDS), [])

    def test_a_domain_names_its_subject_and_its_intent_clauses(self):
        # extract-billing: what Jev's contexts call the domain, and its clauses in the intent options; nothing else.
        domain = '\n[domain]\nid = "x"\nsubject = "x things"\n\n[domain.intent]\nprocedural = "a"\nmixed = "b"\ndata = "c"\n'
        self.add("x", manifest("x") + domain)
        self.assertEqual([], plugins.discover()["x"].problems)
        self.add("y", manifest("y") + domain.replace('x"', 'y"').replace('data = "c"', 'data = "c"\nchitchat = "d"'))
        self.assertTrue(any("unknown key `chitchat`" in p for p in plugins.discover()["y"].problems), plugins.discover()["y"].problems)

    def test_a_provider_names_what_it_provides(self):
        self.add("jev", manifest("jev", kind="provider"))
        self.assertIn("/provides: a provider plugin names what it provides", plugins.discover()["jev"].problems)

    def test_a_leading_underscore_names_a_plugin_installed_only_when_asked_for(self):
        self.add("_sample")
        self.assertEqual([], plugins.discover()["_sample"].problems)
        self.assertEqual([], self.resolved())
        self.add("__twice", manifest("__twice"))
        self.assertTrue(any(p.startswith("/name") for p in plugins.discover()["__twice"].problems))

    def test_the_folder_and_the_name_agree(self):
        self.add("x", manifest("y"))
        self.assertTrue(any("differs from the folder name" in p for p in plugins.discover()["x"].problems))


class InstallTests(PluginsTestCase):
    def test_install_writes_the_set_and_the_lb_snippets_by_rename(self):
        self.add("weather", **{"lb.http.conf": "upstream w { server weather:8080; }\n",
                               "lb.server.conf": "location = /weather/mcp { proxy_pass http://w/mcp; }\n",
                               "server.json": '{"remotes":[{"url":"http://lb/weather/mcp"}]}'})
        plugins.install()
        document = json.loads((self.root / ".installed").read_text())
        self.assertEqual("weather", document["plugins"][0]["manifest"]["name"])
        self.assertEqual("http://lb/weather/mcp", document["plugins"][0]["serverJson"]["remotes"][0]["url"])
        self.assertTrue((self.lb / "conf.d/http/00-api.conf").is_file())
        self.assertTrue((self.lb / "conf.d/http/50-weather.conf").is_file())
        self.assertTrue((self.lb / "conf.d/server/50-weather.conf").is_file())
        self.assertEqual([], list(self.root.glob(".*.tmp-*")))

    def test_a_plugin_no_longer_installed_leaves_no_snippet(self):
        self.add("weather", **{"lb.server.conf": "location = /weather/mcp { return 200; }\n"})
        plugins.install()
        os.environ["MAF_PLUGINS"] = "none"
        plugins.install()
        self.assertFalse((self.lb / "conf.d/server/50-weather.conf").exists())
        self.assertTrue((self.lb / "conf.d/http/00-api.conf").is_file())

    def test_services_are_read_from_the_plugins_compose_file(self):
        folder = self.add("weather", **{"compose.yml": "services:\n  weather:\n    image: x\n  weather-db:\n    image: y\nvolumes:\n  data:\n"})
        self.assertEqual(["weather", "weather-db"], plugins.services(plugins.Plugin("weather", folder, {})))

    def test_the_product_variant_keeps_only_plugins_stage_or_prod_allow(self):
        # Fictional plugins: a real plugin's project name may appear nowhere outside its folder (the contract suite).
        self.add("devtool", manifest("devtool"), **{"server__Maf.Lab.Plugins.DevTool.csproj": "<Project/>"})
        self.add("ledger", manifest("ledger", environments=["dev", "prod"]), **{"server__Maf.Lab.Plugins.Ledger.csproj": "<Project/>"})
        out = subprocess.run([sys.executable, str(ROOT / "scripts/plugins.py"), "product-servers"], capture_output=True, text=True,
                             env=os.environ.copy(), check=True).stdout.strip()
        self.assertEqual("|Maf.Lab.Plugins.Ledger|", out)


@unittest.skipUnless(shutil.which("docker"), "docker is not installed")
class ComposeTests(PluginsTestCase):
    """A plugin's compose file holds only its own services: the core services are byte-identical with any set (task 2.3)."""

    def core_services(self, *plugin_files: Path) -> dict:
        files = [ROOT / "compose/docker-compose.yml", ROOT / "compose/docker-compose.dev.yml", *plugin_files]
        env = dict(os.environ, COMPOSE_FILE=os.pathsep.join(str(f) for f in files), MAF_LAB_REPO=str(ROOT))
        out = subprocess.run(["docker", "compose", "-p", "maf-lab-config-test", "config", "--format", "json"],
                             capture_output=True, text=True, env=env)
        if out.returncode != 0:
            self.skipTest(f"docker compose config failed: {out.stderr.strip()[:200]}")
        services = json.loads(out.stdout)["services"]
        core = json.loads(subprocess.run(["docker", "compose", "-p", "maf-lab-config-test", "config", "--format", "json"],
                                         capture_output=True, text=True, check=True,
                                         env=dict(env, COMPOSE_FILE=os.pathsep.join(str(f) for f in files[:2]))).stdout)["services"]
        return {name: services[name] for name in core}

    def test_the_core_services_are_identical_for_none_all_and_each_single_plugin(self):
        a = self.add("alpha", **{"compose.yml": "services:\n  alpha:\n    image: nginx:1.30.5-alpine\n"}) / "compose.yml"
        b = self.add("beta", **{"compose.yml": "services:\n  beta:\n    image: nginx:1.30.5-alpine\n"}) / "compose.yml"
        none = self.core_services()
        for files in ([a], [b], [a, b]):
            self.assertEqual(none, self.core_services(*files), files)


if __name__ == "__main__":
    unittest.main()


class NewPluginTests(PluginsTestCase):
    """make plugin-new (introduce-plugins 6.2): KIND=mcp copies _example under the new name, KIND=app renders the app
    template; both validate, neither keeps the template's name, and a bad request is refused before anything is written."""

    def setUp(self):
        super().setUp()
        shutil.copytree(ROOT / "plugins" / plugins.EXAMPLE_NAME, self.root / plugins.EXAMPLE_NAME,
                        ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))

    def residue(self, folder: Path, word: str) -> list[str]:
        return [f"{p.relative_to(folder)}: {line.strip()}" for p in folder.rglob("*") if p.is_file()
                for line in p.read_text(encoding="utf-8").splitlines()
                if word in line.lower() and plugins.SECTION_MARKER not in line] + \
               [str(p.relative_to(folder)) for p in folder.rglob("*") if word in p.name.lower()]

    def test_an_mcp_plugin_is_the_template_under_its_own_name(self):
        written = plugins.new_plugin("weather-report", "mcp")
        folder = self.root / "weather-report"
        self.assertIn("service/WeatherReport.McpServer.csproj", written)
        self.assertEqual([], plugins.discover()["weather-report"].problems)
        self.assertEqual([], self.residue(folder, "example"))
        toml = (folder / "plugin.toml").read_text(encoding="utf-8")
        self.assertIn('question_key = "in_weather_report"', toml)
        self.assertIn('tools = ["get_weather_report_fact"]', toml)
        self.assertIn("location = /weather-report/mcp", (folder / "lb.server.conf").read_text(encoding="utf-8"))
        self.assertTrue((folder / "prompt.md").read_text(encoding="utf-8").count(plugins.SECTION_MARKER) == 1)
        # Not opt-in: an ordinary plugin MAF_PLUGINS unset installs.
        self.assertEqual(["weather-report"], self.resolved())

    def test_an_app_plugin_is_rendered_from_the_template(self):
        written = plugins.new_plugin("notes", "app")
        folder = self.root / "notes"
        self.assertEqual(sorted(["docs/http-api.md", "plugin.toml", "server/Maf.Lab.Plugins.Notes.csproj", "server/NotesPlugin.cs",
                                 "tests/unit/NotesPluginTests.cs", "web/NotesPage.test.tsx", "web/NotesPage.tsx",
                                 "web/index.ts"]), written)
        self.assertEqual([], plugins.discover()["notes"].problems)
        self.assertEqual([], [p for p in folder.rglob("*") if "$" in p.name or "__Name__" in p.name])
        self.assertIn('"/api/notes/summary"', (folder / "server" / "NotesPlugin.cs").read_text(encoding="utf-8"))

    def test_a_bad_request_is_refused_and_writes_nothing(self):
        plugins.new_plugin("notes", "app")
        for name, kind, says in [("notes", "app", "already exists"), ("_mine", "app", "reserved"),
                                 ("Bad_Name", "mcp", "not a plugin name"), ("9lives", "mcp", "not a plugin name"),
                                 ("fine", "a2a", "KIND=")]:
            with self.subTest(name=name, kind=kind):
                with self.assertRaises(plugins.PluginError) as raised:
                    plugins.new_plugin(name, kind)
                self.assertIn(says, str(raised.exception))
        self.assertEqual(sorted([plugins.EXAMPLE_NAME, "notes"]), sorted(p.name for p in self.root.iterdir()))

    def test_the_command_exits_2_with_one_line_naming_the_fix(self):
        result = subprocess.run([sys.executable, str(ROOT / "scripts" / "plugins.py"), "new", "x", "a2a"],
                                capture_output=True, text=True, env={**os.environ})
        self.assertEqual(2, result.returncode)
        self.assertIn("make plugin-new NAME=<name> KIND=mcp|app", result.stderr)
