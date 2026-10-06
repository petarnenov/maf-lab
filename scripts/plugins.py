#!/usr/bin/env python3
"""
The plugin set of this deployment (introduce-plugins decisions 1-4): which plugins are installed, what they bring, and
the files the core services read at run time.

  plugins.py resolve        print the installed set, one name per line (fails on a cycle, a missing plugin or an
                            environment the plugin does not allow, naming the plugin)
  plugins.py install        resolve, then write plugins/.installed by rename and regenerate compose/lb/conf.d
  plugins.py compose-files  print each installed plugin's compose.yml, absolute, one per line
  plugins.py list           the catalogue: every plugin with kind, scope, environments, dependencies, installed
  plugins.py validate       check every manifest against plugins/plugin.schema.json (exit 1, naming each problem)
  plugins.py services NAME  print the compose services a plugin's compose.yml defines
  plugins.py has-server NAME  exit 0 when the plugin has an in-process server part (an api restart is needed)
  plugins.py product-servers  the server projects the product image keeps, as |A|B| for MafProductPlugins

Inputs: MAF_PLUGINS (unset or empty: every bundled plugin allowed in MAF_ENV except _example; "none": no plugin;
otherwise a comma-separated list) and MAF_ENV (dev | qa | stage | prod; default dev). MAF_PLUGINS_ROOT overrides the
plugins directory (tests).

Only the standard library is used, so make, docs.py and CI run it with any Python 3.11+.
"""
from __future__ import annotations

import json
import os
import re
import sys
import tomllib
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ENVIRONMENTS = ("dev", "qa", "stage", "prod")
EXAMPLE = "_example"


class PluginError(Exception):
    """A problem with the plugin set, worded for a person: it names the plugin and what to do."""


@dataclass
class Plugin:
    name: str
    folder: Path
    manifest: dict
    problems: list[str] = field(default_factory=list)

    @property
    def depends(self) -> list[str]:
        return list(self.manifest.get("depends", []))

    @property
    def environments(self) -> list[str]:
        return list(self.manifest.get("environments", []))

    @property
    def has_server(self) -> bool:
        return any((self.folder / "server").glob("*.csproj"))


def plugins_root() -> Path:
    return Path(os.environ.get("MAF_PLUGINS_ROOT") or ROOT / "plugins")


def lb_root() -> Path:
    return Path(os.environ.get("MAF_LB_ROOT") or ROOT / "compose" / "lb")


# ── manifests ────────────────────────────────────────────────────────────────────────────────────────────────────

def load_schema() -> dict:
    return json.loads((ROOT / "plugins" / "plugin.schema.json").read_text(encoding="utf-8"))


# The JSON Schema (draft 2020-12) keywords the validator below implements; a test checks the schema uses no other.
KNOWN_KEYWORDS = frozenset({"$schema", "$id", "title", "description", "type", "required", "properties",
                            "additionalProperties", "enum", "pattern", "minLength", "minItems", "uniqueItems", "items"})


def schema_errors(value, schema: dict, path: str = "") -> list[str]:
    """
    The subset of JSON Schema (draft 2020-12) the manifest schema uses: type, required, properties,
    additionalProperties, enum, pattern, minLength, minItems, uniqueItems and items. The standard library has no JSON
    Schema validator, and the schema stays the source of truth: a keyword this does not know is refused, not ignored.
    """
    unknown = set(schema) - KNOWN_KEYWORDS
    if unknown:
        return [f"{path or '/'}: the schema uses {sorted(unknown)}, which the validator does not implement"]
    errors: list[str] = []
    where = path or "/"
    types = {"object": dict, "array": list, "string": str, "integer": int, "boolean": bool}
    expected = schema.get("type")
    if expected:
        python = types[expected]
        if not isinstance(value, python) or (expected == "integer" and isinstance(value, bool)):
            return [f"{where}: expected {expected}"]
    if "enum" in schema and value not in schema["enum"]:
        errors.append(f"{where}: {value!r} is not one of {schema['enum']}")
    if isinstance(value, str):
        if "minLength" in schema and len(value.strip()) < schema["minLength"]:
            errors.append(f"{where}: must not be empty")
        if "pattern" in schema and not re.search(schema["pattern"], value):
            errors.append(f"{where}: {value!r} does not match {schema['pattern']}")
    if isinstance(value, list):
        if "minItems" in schema and len(value) < schema["minItems"]:
            errors.append(f"{where}: needs at least {schema['minItems']} item(s)")
        if schema.get("uniqueItems") and len({json.dumps(v, sort_keys=True) for v in value}) != len(value):
            errors.append(f"{where}: items must be unique")
        if "items" in schema:
            for i, item in enumerate(value):
                errors += schema_errors(item, schema["items"], f"{path}/{i}")
    if isinstance(value, dict):
        for key in schema.get("required", []):
            if key not in value:
                errors.append(f"{where}: missing required `{key}`")
        properties = schema.get("properties", {})
        extra = schema.get("additionalProperties", True)
        for key, item in value.items():
            if key in properties:
                errors += schema_errors(item, properties[key], f"{path}/{key}")
            elif extra is False:
                errors.append(f"{where}: unknown key `{key}`")
            elif isinstance(extra, dict):
                errors += schema_errors(item, extra, f"{path}/{key}")
    return errors


# The forms a proposal's `## Progress` and `## Stopping` already accept (progress-feedback, stop-anything), on one line.
STOPPING_FACTS = ("Key:", "Stop:", "Recorded in:", "Shown:")


def form_errors(manifest: dict) -> list[str]:
    errors = []
    progress = str(manifest.get("progress", "")).strip()
    if progress and not (re.match(r"^None\s*[—-]\s*\S", progress)
                         or re.match(r"^Not yet\s*[—-]\s*\S.*follow-up\s*:\s*\S", progress, re.I)
                         or re.search(r"\b(Terminal|Page)\s*:\s*\S", progress)):
        errors.append("/progress: say `None — <reason>`, `Not yet — <reason>; follow-up: <change>`, or `Terminal:` "
                      "and/or `Page:` with how it shows (progress-feedback)")
    stopping = str(manifest.get("stopping", "")).strip()
    if stopping and not re.match(r"^None\s*[—-]\s*\S", stopping):
        missing = [fact for fact in STOPPING_FACTS if not re.search(re.escape(fact) + r"\s*\S", stopping)]
        if missing:
            errors.append(f"/stopping: say `None — <reason>`, or give {', '.join(missing)} (stop-anything)")
    return errors


def discover() -> dict[str, Plugin]:
    """Every plugin folder present, by name, each with the problems its manifest has."""
    root = plugins_root()
    found: dict[str, Plugin] = {}
    if not root.is_dir():
        return found
    schema = load_schema()
    for folder in sorted(p for p in root.iterdir() if p.is_dir() and not p.name.startswith(".")):
        toml = folder / "plugin.toml"
        if not toml.is_file():
            continue
        try:
            manifest = tomllib.loads(toml.read_text(encoding="utf-8"))
        except tomllib.TOMLDecodeError as e:
            found[folder.name] = Plugin(folder.name, folder, {}, [f"plugin.toml is not valid TOML: {e}"])
            continue
        problems = schema_errors(manifest, schema) + form_errors(manifest)
        if manifest.get("name") not in (None, folder.name):
            problems.append(f"/name: {manifest.get('name')!r} differs from the folder name {folder.name!r}")
        if manifest.get("kind") == "provider" and not manifest.get("provides"):
            problems.append("/provides: a provider plugin names what it provides")
        found[folder.name] = Plugin(folder.name, folder, manifest, problems)
    return found


# ── the installed set ────────────────────────────────────────────────────────────────────────────────────────────

def environment() -> str:
    env = (os.environ.get("MAF_ENV") or "dev").strip()
    if env not in ENVIRONMENTS:
        raise PluginError(f"MAF_ENV={env!r} is not one of {', '.join(ENVIRONMENTS)}")
    return env


def resolve(available: dict[str, Plugin] | None = None) -> list[Plugin]:
    """The installed plugins, dependencies first. Raises PluginError naming the plugin for every refusal."""
    available = discover() if available is None else available
    env = environment()
    raw = (os.environ.get("MAF_PLUGINS") or "").strip()
    if raw == "none":
        requested: list[str] = []
    elif raw:
        requested = [n.strip() for n in raw.split(",") if n.strip()]
    else:
        requested = [n for n, p in available.items() if n != EXAMPLE and env in p.environments]

    order: list[str] = []
    visiting: list[str] = []

    def visit(name: str, wanted_by: str | None) -> None:
        if name in order:
            return
        if name in visiting:
            cycle = " → ".join(visiting[visiting.index(name):] + [name])
            raise PluginError(f"plugin dependency cycle: {cycle}")
        plugin = available.get(name)
        if plugin is None:
            by = f" (needed by {wanted_by})" if wanted_by else ""
            raise PluginError(f"plugin '{name}' does not exist{by}: no plugins/{name}/plugin.toml")
        if plugin.problems:
            raise PluginError(f"plugin '{name}' has an invalid manifest: " + "; ".join(plugin.problems))
        if env not in plugin.environments:
            raise PluginError(f"plugin '{name}' is not allowed in MAF_ENV={env} (it allows {', '.join(plugin.environments)})")
        visiting.append(name)
        for dependency in plugin.depends:
            visit(dependency, name)
        visiting.pop()
        order.append(name)

    for name in requested:
        visit(name, None)
    return [available[n] for n in order]


def server_json(plugin: Plugin) -> dict | None:
    path = plugin.folder / "server.json"
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None


def installed_document(plugins: list[Plugin]) -> dict:
    return {
        "schema": 1,
        "env": environment(),
        "plugins": [{"manifest": p.manifest, "serverJson": server_json(p), "hasServer": p.has_server} for p in plugins],
    }


def write_by_rename(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f".{path.name}.tmp-{os.getpid()}")
    tmp.write_text(text, encoding="utf-8")
    os.replace(tmp, path)


def regenerate_conf_d(plugins: list[Plugin]) -> None:
    """
    compose/lb/conf.d is derived, never state (decision 2): the api upstream from its tracked template, then each
    installed plugin's lb parts. Every file is written by rename, both parts before any reload, and anything a plugin
    no longer installed left behind is removed.
    """
    conf_d = lb_root() / "conf.d"
    wanted: dict[Path, str] = {
        conf_d / "http" / "00-api.conf": (lb_root() / "api.upstream.conf").read_text(encoding="utf-8"),
    }
    for p in plugins:
        for part in ("http", "server"):
            snippet = p.folder / f"lb.{part}.conf"
            if snippet.is_file():
                wanted[conf_d / part / f"50-{p.name}.conf"] = snippet.read_text(encoding="utf-8")
    for path, text in wanted.items():
        write_by_rename(path, text)
    for part in ("http", "server"):
        for stale in (conf_d / part).glob("*.conf") if (conf_d / part).is_dir() else []:
            if stale not in wanted:
                stale.unlink()


def install() -> list[Plugin]:
    plugins = resolve()
    write_by_rename(plugins_root() / ".installed", json.dumps(installed_document(plugins), indent=2) + "\n")
    regenerate_conf_d(plugins)
    return plugins


def services(plugin: Plugin) -> list[str]:
    """The services a plugin's compose.yml defines: top-level `services:` keys, read without a YAML library."""
    compose = plugin.folder / "compose.yml"
    if not compose.is_file():
        return []
    names, inside = [], False
    for line in compose.read_text(encoding="utf-8").splitlines():
        if re.match(r"^services:\s*$", line):
            inside = True
        elif re.match(r"^\S", line):
            inside = False
        elif inside and (m := re.match(r"^  ([A-Za-z0-9_.-]+):\s*$", line)):
            names.append(m.group(1))
    return names


# ── commands ─────────────────────────────────────────────────────────────────────────────────────────────────────

def main(argv: list[str]) -> int:
    command = argv[1] if len(argv) > 1 else "resolve"
    try:
        if command == "resolve":
            for p in resolve():
                print(p.name)
        elif command == "install":
            # --conf-d-only / --installed-only split the two writes, so plugin-on can reload the balancer with a
            # plugin's routes before the core services are told it is installed (decision 2's order).
            only = argv[2] if len(argv) > 2 else ""
            plugins = resolve()
            if only != "--conf-d-only":
                write_by_rename(plugins_root() / ".installed", json.dumps(installed_document(plugins), indent=2) + "\n")
            if only != "--installed-only":
                regenerate_conf_d(plugins)
            if not only:
                print(f"✓ plugins installed ({environment()}): {', '.join(p.name for p in plugins) or 'none'}")
        elif command == "compose-files":
            for p in resolve():
                if (p.folder / "compose.yml").is_file():
                    print(p.folder / "compose.yml")
        elif command == "list":
            available = discover()
            installed = set()
            try:
                installed = {p.name for p in resolve(available)}
            except PluginError as e:
                print(f"⚠ {e}", file=sys.stderr)
            if not available:
                print("no plugins (plugins/<name>/plugin.toml)")
            for name, p in available.items():
                m = p.manifest
                state = "installed" if name in installed else "-"
                if p.problems:
                    state = "invalid: " + "; ".join(p.problems)
                print(f"{name:<22} {m.get('kind', '?'):<9} {m.get('scope', '?'):<13} {','.join(p.environments):<16} "
                      f"{state:<10} depends={','.join(p.depends) or '-'}  {m.get('description', '')}")
        elif command == "validate":
            problems = [f"plugins/{n}/plugin.toml: {pr}" for n, p in discover().items() for pr in p.problems]
            for line in problems:
                print(line, file=sys.stderr)
            return 1 if problems else 0
        elif command == "services":
            plugin = discover().get(argv[2])
            if plugin is None:
                raise PluginError(f"plugin '{argv[2]}' does not exist")
            for s in services(plugin):
                print(s)
        elif command == "manifest-json":
            # One plugin's manifest as the core reads it (plugins/.installed's shape), for the contract suite.
            plugin = discover().get(argv[2])
            if plugin is None:
                raise PluginError(f"plugin '{argv[2]}' does not exist")
            print(json.dumps({"manifest": plugin.manifest, "serverJson": server_json(plugin), "hasServer": plugin.has_server}))
        elif command == "product-servers":
            # The server projects the product image variant keeps (decision 5e): plugins that allow stage or prod.
            names = [csproj.stem for p in discover().values() if {"stage", "prod"} & set(p.environments)
                     for csproj in (p.folder / "server").glob("*.csproj")]
            print("|" + "|".join(names) + "|")
        elif command == "product-plugins":
            # The plugins the product image variant keeps (decision 5e), as |a|b|: those whose manifest allows stage or prod.
            print("|" + "|".join(n for n, p in discover().items() if {"stage", "prod"} & set(p.environments)) + "|")
        elif command == "has-server":
            plugin = discover().get(argv[2])
            return 0 if plugin is not None and plugin.has_server else 1
        else:
            print(__doc__, file=sys.stderr)
            return 2
    except PluginError as e:
        print(f"✗ {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
