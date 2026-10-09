#!/usr/bin/env python3
"""Keep the documents outside the main specs in step with the code (openspec/specs/documentation-sync).

    docs.py generate   rewrite every generated block from its source            (make docs)
    docs.py check      change nothing; list every place a document disagrees     (make docs-check)

Standard library only, so it runs wherever python3 does: no .NET SDK, no Docker, no pip install.
A finding prints as `path:line: rule: message → fix`; `check` exits 1 when there is any.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tomllib
import urllib.parse
from dataclasses import dataclass
from pathlib import Path

CONFIG = "docs/docs-sync.toml"
VERBS = ("GET", "POST", "PUT", "PATCH", "DELETE")
ROUTE_SOURCES = ("src/Maf.Lab.Api", "src/Maf.Lab.Hosting")
# Every plugin's in-process part maps routes too (introduce-plugins task 3.6).
PLUGIN_ROUTE_SOURCES = "plugins/*/server"
HTTP_API = "docs/http-api.md"

# Where each generated block must appear. Dependency order: repo-layout rewrites project.md, which project-context copies.
REQUIRED_BLOCKS = {
    "README.md": ("make-targets", "lb-routes", "plugins"),
    ".github/copilot-instructions.md": ("lb-routes",),
    "openspec/project.md": ("repo-layout",),
    "openspec/config.yaml": ("project-context",),
    HTTP_API: ("plugin-routes",),
}
BLOCK_ORDER = ("make-targets", "repo-layout", "lb-routes", "plugins", "plugin-routes", "project-context")
GENERATED_FILES = ("README.md", ".github/copilot-instructions.md", "openspec/project.md", "openspec/config.yaml", HTTP_API)


@dataclass(frozen=True)
class Finding:
    path: str
    line: int | None
    rule: str
    message: str
    fix: str

    def __str__(self) -> str:
        where = f"{self.path}:{self.line}" if self.line else self.path
        return f"{where}: {self.rule}: {self.message} → {self.fix}"


class Repo:
    """The repository as the rules see it; reads go through a cache so generated text can be checked in memory."""

    def __init__(self, root: Path):
        self.root = root
        self._text: dict[str, str] = {}
        self.config = tomllib.loads(self.read(CONFIG)) if self.exists(CONFIG) else {}

    def exists(self, rel: str) -> bool:
        return rel in self._text or (self.root / rel).is_file()

    def read(self, rel: str) -> str:
        if rel not in self._text:
            self._text[rel] = (self.root / rel).read_text(encoding="utf-8")
        return self._text[rel]

    def put(self, rel: str, text: str) -> None:
        self._text[rel] = text

    def tracked_dirs(self, under: str = "") -> list[str]:
        """Directories directly under `under` that hold at least one file git tracks (all of them outside a repo)."""
        base = self.root / under if under else self.root
        try:
            out = subprocess.run(["git", "ls-files", "--", under or "."], cwd=self.root, capture_output=True,
                                 text=True, check=True).stdout
            inside = subprocess.run(["git", "rev-parse", "--show-toplevel"], cwd=self.root, capture_output=True,
                                    text=True, check=True).stdout.strip()
            if Path(inside).resolve() != self.root.resolve():
                raise subprocess.CalledProcessError(1, "git")
            depth = len(Path(under).parts) if under else 0
            names = {Path(p).parts[depth] for p in out.splitlines() if len(Path(p).parts) > depth + 1}
        except (subprocess.CalledProcessError, FileNotFoundError):
            names = {p.name for p in base.iterdir() if p.is_dir() and p.name != ".git"} if base.is_dir() else set()
        return sorted(n for n in names if (base / n).is_dir())

    def checked_documents(self) -> list[str]:
        docs = ["README.md", "CLAUDE.md", "openspec/project.md", ".github/copilot-instructions.md"]
        docs_dir = self.root / "docs"
        if docs_dir.is_dir():
            docs += sorted(str(p.relative_to(self.root)) for p in docs_dir.rglob("*.md"))
        return [d for d in docs if self.exists(d)]


# ── generated blocks ───────────────────────────────────────────────────────────────────────────────────────────────

MD_START = re.compile(r"^<!-- generated:([a-z0-9-]+)\b.*-->\s*$")
MD_END = re.compile(r"^<!-- /generated:([a-z0-9-]+) -->\s*$")
YAML_START = re.compile(r"^# generated:([a-z0-9-]+)\b.*$")
YAML_END = re.compile(r"^# /generated:([a-z0-9-]+)\s*$")


def _patterns(rel: str):
    return (YAML_START, YAML_END) if rel.endswith((".yaml", ".yml")) else (MD_START, MD_END)


def find_blocks(rel: str, text: str) -> tuple[list[tuple[str, int, int]], list[Finding]]:
    """(name, start line index, end line index) of every block, and findings for unbalanced markers."""
    start_re, end_re = _patterns(rel)
    blocks, findings, open_block = [], [], None
    for i, line in enumerate(text.split("\n")):
        if m := start_re.match(line):
            if open_block:
                findings.append(Finding(rel, i + 1, "generated-block", f"`{m[1]}` starts inside `{open_block[0]}`",
                                        "close the first block before opening another"))
            open_block = (m[1], i)
        elif m := end_re.match(line):
            if not open_block or open_block[0] != m[1]:
                findings.append(Finding(rel, i + 1, "generated-block", f"end marker for `{m[1]}` has no matching start",
                                        "restore the start marker, or delete this one"))
            else:
                blocks.append((m[1], open_block[1], i))
            open_block = None
    if open_block:
        findings.append(Finding(rel, open_block[1] + 1, "generated-block", f"`{open_block[0]}` is never closed",
                                f"add the end marker for `{open_block[0]}`"))
    return blocks, findings


def replace_block(text: str, start: int, end: int, body: str) -> str:
    lines = text.split("\n")
    return "\n".join(lines[: start + 1] + ([body] if body else []) + lines[end:])


def make_files(repo: Repo) -> list[str]:
    """The Makefile and every plugin's own make targets, which it includes (introduce-plugins task 3.6)."""
    return ["Makefile"] + sorted(str(p.relative_to(repo.root)) for p in repo.root.glob("plugins/*/plugin.mk"))


def gen_make_targets(repo: Repo, findings: list[Finding]) -> str:
    rows = ["| Command | What it does |", "|---|---|"]
    for rel in make_files(repo):
        for m in re.finditer(r"^([a-zA-Z0-9_-]+):.*?## (.*)$", repo.read(rel), re.M):
            description = m[2].strip().replace("|", r"\|")
            rows.append(f"| `make {m[1]}` | {description} |")
    return "\n".join(rows)


def project_description(repo: Repo, rel_dir: str) -> str | None:
    for csproj in sorted((repo.root / rel_dir).glob("*.csproj")):
        if m := re.search(r"<Description>(.*?)</Description>", csproj.read_text(encoding="utf-8"), re.S):
            return " ".join(m[1].split())
        return None
    return repo.config.get("layout", {}).get(rel_dir)


def gen_repo_layout(repo: Repo, findings: list[Finding]) -> str:
    layout = repo.config.get("layout", {})
    entries: list[tuple[int, str, str]] = []
    for top in repo.tracked_dirs():
        entries.append((1, f"{top}/", layout.get(top) or ""))
        if not layout.get(top):
            findings.append(Finding(CONFIG, None, "layout", f"top-level directory `{top}/` has no description",
                                    f'add `"{top}" = "…"` under [layout]'))
        if top in ("src", "tools"):
            for sub in repo.tracked_dirs(top):
                rel = f"{top}/{sub}"
                desc = project_description(repo, rel)
                if not desc:
                    is_dotnet = any((repo.root / rel).glob("*.csproj"))
                    findings.append(Finding(rel, None, "layout", f"`{rel}` has no description",
                                            "add <Description> to its .csproj" if is_dotnet
                                            else f'add `"{rel}" = "…"` under [layout] in {CONFIG}'))
                entries.append((2, f"{sub}/", desc or ""))
    known = {d for d in repo.tracked_dirs()} | {f"{t}/{s}" for t in ("src", "tools") for s in repo.tracked_dirs(t)}
    for key in layout:
        if key not in known:
            findings.append(Finding(CONFIG, None, "layout", f"[layout] describes `{key}`, which git does not track",
                                    "remove the entry"))
    width = max((2 * depth + len(name) for depth, name, _ in entries), default=0) + 2
    lines = ["```", "maf-lab/"]
    for depth, name, desc in entries:
        head = "  " * depth + name
        lines.append((head.ljust(width) + desc).rstrip())
    lines.append("```")
    return "\n".join(lines)


def lb_sources(repo: Repo) -> list[tuple[str, str | None]]:
    """
    The balancer's configuration as nginx assembles it (introduce-plugins decision 4): nginx.conf, the tracked api
    upstream template that `make up` copies into conf.d, and each plugin's own snippets — read from their sources, so
    the check needs no stack and no generated conf.d. Each source names the plugin it belongs to, or None for the core.
    """
    sources: list[tuple[str, str | None]] = [("compose/lb/nginx.conf", None)]
    if repo.exists("compose/lb/api.upstream.conf"):
        sources.append(("compose/lb/api.upstream.conf", None))
    for part in ("http", "server"):
        for p in sorted(repo.root.glob(f"plugins/*/lb.{part}.conf")):
            sources.append((str(p.relative_to(repo.root)), p.parent.name))
    return sources


def gen_lb_routes(repo: Repo, findings: list[Finding]) -> str:
    sources = lb_sources(repo)
    upstreams = {m[1]: m[2] for rel, _ in sources
                 for m in re.finditer(r"upstream\s+(\w+)\s*\{[^}]*?^\s*server\s+([\w.-]+)", repo.read(rel), re.M | re.S)}
    rows = ["| Path | Match | Served by |", "|---|---|---|"]
    for rel, plugin in sources:
        for m in re.finditer(r"^\s*location\s+(=|~\*?|\^~)?\s*(\S+)\s*\{(.*?)^\s*\}", repo.read(rel), re.M | re.S):
            row = lb_row(repo, rel, plugin, m, upstreams, findings)
            if row:
                rows.append(row)
    return "\n".join(rows)


def lb_row(repo: Repo, rel: str, plugin: str | None, m: re.Match, upstreams: dict[str, str], findings: list[Finding]) -> str | None:
        modifier, path, body = m[1] or "", m[2], m[3]
        exact = modifier == "="
        if p := re.search(r"proxy_pass\s+http://(\w+)(/\S*)?;", body):
            service = upstreams.get(p[1])
            if not service:
                findings.append(Finding(rel, None, "lb-routes",
                                        f"`location {path}` proxies to unknown upstream `{p[1]}`", "define the upstream"))
                service = p[1]
            target = f"`{service}`" + (f" at `{p[2]}`" if p[2] else "")
        elif re.search(r"\breturn\b", body):
            target = "the balancer itself"
        else:
            return None
        if plugin:
            target += f" (plugin `{plugin}`)"
        match = "exact" if exact else "regex" if modifier.startswith("~") else "prefix"
        return f"| `{path.replace('|', chr(92) + '|')}` | {match} | {target} |"


def gen_plugins(repo: Repo, findings: list[Finding]) -> str:
    """Every plugin present, from its manifest (introduce-plugins task 3.6); a bad manifest is a finding, not a row."""
    found = discover_plugins(repo)
    if not found:
        return "No plugin is present yet: the repository holds only the core (`plugins/` has no `plugin.toml`)."
    rows = ["| Plugin | Kind | Scope | Environments | What it is |", "|---|---|---|---|---|"]
    for name, p in found.items():
        if p.problems:
            continue
        m = p.manifest
        rows.append(f"| `{name}` | {m['kind']} | {m['scope']} | {', '.join(m['environments'])} | "
                    f"{m['description'].replace('|', chr(92) + '|')} |")
    return "\n".join(rows)


PLUGIN_ROUTE_DOCS = "plugins/*/docs/http-api.md"


def gen_plugin_routes(repo: Repo, findings: list[Finding]) -> str:
    """Each plugin's routes, as it documents them in its own folder (introduce-plugins 6.2): a heading per plugin and its
    text, so the core's API document is whole while the rows leave with the plugin's folder."""
    sections = []
    for path in sorted(repo.root.glob(PLUGIN_ROUTE_DOCS)):
        name = path.parent.parent.name
        text = repo.read(str(path.relative_to(repo.root))).strip()
        sections.append(f"### {name}\n\n{text}")
    return "\n\n".join(sections) if sections else "No plugin present serves a route of its own."


def plugin_module():
    """scripts/plugins.py, imported: the one reader of plugin manifests, shared with make."""
    import importlib.util
    path = Path(__file__).resolve().parent / "plugins.py"
    spec = importlib.util.spec_from_file_location("maf_plugins", path)
    if not spec or not spec.loader:
        return None
    if "maf_plugins" in sys.modules:
        return sys.modules["maf_plugins"]
    module = importlib.util.module_from_spec(spec)
    sys.modules["maf_plugins"] = module  # a dataclass resolves its module by name while it is defined
    spec.loader.exec_module(module)
    return module


def discover_plugins(repo: Repo) -> dict:
    """The plugin folders of the repository being checked (not necessarily this checkout), with their problems."""
    plugins = plugin_module()
    if not plugins:
        return {}
    old = os.environ.get("MAF_PLUGINS_ROOT")
    os.environ["MAF_PLUGINS_ROOT"] = str(repo.root / "plugins")
    try:
        return plugins.discover()
    finally:
        if old is None:
            os.environ.pop("MAF_PLUGINS_ROOT", None)
        else:
            os.environ["MAF_PLUGINS_ROOT"] = old


def check_plugin_manifests(repo: Repo) -> list[Finding]:
    """Every manifest's schema/progress/stopping; missing dependencies fail explicit deployment selections.

    With the automatic set a removed folder deactivates its dependants, as plugins.resolve does.
    """
    found = discover_plugins(repo)
    findings = [Finding(f"plugins/{name}/plugin.toml", None, "plugins", problem,
                        "fix the manifest (plugins/plugin.schema.json, docs/plugins.md)")
                for name, p in found.items() for problem in p.problems]
    raw = (os.environ.get("MAF_PLUGINS") or "").strip()
    selected = {n.strip() for n in raw.split(",") if n.strip()} if raw not in ("", "none") else set()
    while True:
        dependencies = {d for n in selected if n in found for d in found[n].depends}
        if dependencies <= selected:
            break
        selected |= dependencies
    for name, p in found.items():
        for dependency in p.depends:
            if dependency not in found and name in selected:
                findings.append(Finding(f"plugins/{name}/plugin.toml", None, "plugins",
                                        f"depends on `{dependency}`, which has no plugins/{dependency}/plugin.toml",
                                        "add that plugin or remove the dependency"))
    return findings


def gen_project_context(repo: Repo, findings: list[Finding]) -> str:
    lines = repo.read("openspec/project.md").rstrip("\n").split("\n")
    return "\n".join(["context: |"] + [("  " + l).rstrip() for l in lines])


GENERATORS = {
    "make-targets": gen_make_targets,
    "repo-layout": gen_repo_layout,
    "lb-routes": gen_lb_routes,
    "plugins": gen_plugins,
    "plugin-routes": gen_plugin_routes,
    "project-context": gen_project_context,
}


def render(repo: Repo) -> tuple[dict[str, str], list[Finding]]:
    """The generated files as `make docs` would write them, and findings about the blocks themselves."""
    findings: list[Finding] = []
    bodies: dict[str, str] = {}
    files = [f for f in GENERATED_FILES if repo.exists(f)] + [
        d for d in repo.checked_documents() if d not in GENERATED_FILES]
    for name in BLOCK_ORDER:
        for rel in files:
            blocks, _ = find_blocks(rel, repo.read(rel))
            for block_name, start, end in reversed(blocks):
                if block_name != name:
                    continue
                if name not in bodies:
                    bodies[name] = GENERATORS[name](repo, findings)
                repo.put(rel, replace_block(repo.read(rel), start, end, bodies[name]))
    for rel in files:
        blocks, marker_findings = find_blocks(rel, repo.read(rel))
        findings += marker_findings
        for block_name, start, _ in blocks:
            if block_name not in GENERATORS:
                findings.append(Finding(rel, start + 1, "generated-block", f"unknown block `{block_name}`",
                                        f"use one of: {', '.join(BLOCK_ORDER)}"))
    for rel, names in REQUIRED_BLOCKS.items():
        if not repo.exists(rel):
            continue
        present = {b[0] for b in find_blocks(rel, repo.read(rel))[0]}
        for name in names:
            if name not in present:
                findings.append(Finding(rel, None, "generated-block", f"block `{name}` is missing",
                                        f"add the `generated:{name}` start and end markers, then run make docs"))
    return {rel: repo.read(rel) for rel in files}, findings


# ── routes ─────────────────────────────────────────────────────────────────────────────────────────────────────────

def normalize_route(path: str) -> str:
    path = re.sub(r"\{[^}]*\}", "{}", path.split("?")[0].strip())
    path = re.sub(r"/+", "/", "/" + path.lstrip("/"))
    return path.rstrip("/") or "/"


def registered_routes(repo: Repo) -> tuple[dict[tuple[str, str], tuple[str, int]], list[Finding]]:
    roots = [repo.root / src for src in ROUTE_SOURCES] + sorted(repo.root.glob(PLUGIN_ROUTE_SOURCES))
    files = sorted(p for src in roots if src.is_dir() for p in src.rglob("*.cs")
                   if "/obj/" not in str(p) and "/bin/" not in str(p))
    consts: dict[str, str] = {}
    texts = {}
    for f in files:
        text = f.read_text(encoding="utf-8")
        texts[f] = text
        classes = [(m.start(), m[1]) for m in re.finditer(r"\b(?:class|record|struct)\s+(\w+)", text)]
        for m in re.finditer(r"\bconst\s+string\s+(\w+)\s*=\s*\"([^\"]*)\"", text):
            owner = next((name for pos, name in reversed(classes) if pos < m.start()), None)
            consts[m[1]] = m[2]
            if owner:
                consts[f"{owner}.{m[1]}"] = m[2]

    def resolve(arg: str) -> str | None:
        arg = arg.strip()
        if m := re.fullmatch(r'@?"([^"]*)"', arg):
            return m[1]
        return consts.get(arg) if re.fullmatch(r"[\w.]+", arg) else None

    routes: dict[tuple[str, str], tuple[str, int]] = {}
    findings: list[Finding] = []
    for f, text in texts.items():
        rel = str(f.relative_to(repo.root))
        groups: dict[str, str] = {}
        for i, raw in enumerate(text.split("\n"), 1):
            line = re.sub(r"^\s*//.*$", "", raw)
            if m := re.search(r"\bvar\s+(\w+)\s*=\s*(\w+)\.MapGroup\(\s*([^,)]*)", line):
                prefix = resolve(m[3])
                if prefix is None:
                    findings.append(Finding(rel, i, "routes", f"cannot resolve MapGroup argument `{m[3].strip()}`",
                                            "use a string literal or a const string"))
                    continue
                groups[m[1]] = groups.get(m[2], "") + prefix
                continue
            if re.search(r"\.MapGroup\(", line):
                findings.append(Finding(rel, i, "routes", "MapGroup result is not held in a `var`",
                                        "assign the group to a local variable so its prefix can be followed"))
                continue
            for m in re.finditer(r"(\w+)\.Map(Get|Post|Put|Patch|Delete|Methods|Fallback|HealthChecks)\(\s*([^,)]*)", line):
                receiver, verb, arg = m[1], m[2], m[3]
                # ASP.NET Core's health checks endpoint answers GET (extract-topology-plugin D1).
                verb = "Get" if verb == "HealthChecks" else verb
                if verb in ("Methods", "Fallback"):
                    findings.append(Finding(rel, i, "routes", f"Map{verb} is not understood by the route check",
                                            "register with MapGet/MapPost/… or extend scripts/docs.py"))
                    continue
                path = resolve(arg)
                if path is None:
                    findings.append(Finding(rel, i, "routes", f"cannot resolve route argument `{arg.strip()}`",
                                            "use a string literal or a const string"))
                    continue
                routes.setdefault((verb.upper(), normalize_route(groups.get(receiver, "") + "/" + path)), (rel, i))
    return routes, findings


def documented_routes(repo: Repo) -> dict[tuple[str, str], int]:
    rows: dict[tuple[str, str], int] = {}
    if not repo.exists(HTTP_API):
        return rows
    for i, line in enumerate(repo.read(HTTP_API).split("\n"), 1):
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in re.split(r"(?<!\\)\|", line.strip().strip("|"))]
        if len(cells) < 2:
            continue
        methods = cells[0].split("/")
        if not all(m in VERBS for m in methods):
            continue
        for path in re.findall(r"`([^`]+)`", cells[1]):
            for method in methods:
                rows.setdefault((method, normalize_route(path) if "…" not in path else path), i)
    return rows


def check_routes(repo: Repo) -> tuple[list[Finding], int]:
    registered, findings = registered_routes(repo)
    documented = documented_routes(repo)
    routes_cfg = repo.config.get("routes", {})
    undocumented = dict(routes_cfg.get("undocumented", {}))
    library = {**routes_cfg.get("library", {}), **routes_cfg.get("elsewhere", {})}
    # SDK bindings belong to the plugin that maps them. Deleting its folder also removes its exemptions.
    for config in sorted(repo.root.glob("plugins/*/docs/docs-sync.toml")):
        plugin_routes = tomllib.loads(repo.read(str(config.relative_to(repo.root)))).get("routes", {})
        undocumented.update(plugin_routes.get("undocumented", {}))
        library.update(plugin_routes.get("library", {}))
        library.update(plugin_routes.get("elsewhere", {}))

    def library_match(path: str) -> str | None:
        for key in library:
            if key == path or (key.endswith("…") and path.startswith(key[:-1])):
                return key
        return None

    for key, reason in {**undocumented, **library}.items():
        if not str(reason).strip():
            findings.append(Finding(CONFIG, None, "routes", f"exemption `{key}` has no reason", "state why"))
    used_library = set()
    for (method, path), (rel, line) in sorted(registered.items()):
        if (method, path) not in documented and f"{method} {path}" not in undocumented:
            findings.append(Finding(rel, line, "routes", f"{method} {path} is registered but not in {HTTP_API}",
                                    f"add a row for it (a plugin's route: to the plugin's docs/http-api.md, then make docs), "
                                    f"or list `{method} {path}` under [routes.undocumented]"))
    for (method, path), line in sorted(documented.items(), key=lambda kv: kv[1]):
        if (method, path) in registered:
            continue
        if key := library_match(path):
            used_library.add(key)
            continue
        findings.append(Finding(HTTP_API, line, "routes", f"{method} {path} is documented but not registered",
                                "remove the row, or list the path under [routes.library] (an SDK registers it) "
                                "or [routes.elsewhere] (another host serves it)"))
    for key in undocumented:
        method, _, path = key.partition(" ")
        if (method, normalize_route(path)) not in registered:
            findings.append(Finding(CONFIG, None, "routes", f"[routes.undocumented] names `{key}`, which is not registered",
                                    "remove the exemption"))
    for key in library:
        if key not in used_library:
            findings.append(Finding(CONFIG, None, "routes", f"route exemption `{key}` matches no row in {HTTP_API}",
                                    "remove the exemption"))
    return findings, len(registered)


# ── make references, models, links, change proposals ─────────────────────────────────────────────────────────────

def make_targets_defined(repo: Repo) -> set[str]:
    targets = set()
    for rel in make_files(repo):
        for m in re.finditer(r"^([a-zA-Z0-9_.%-][^:=#\n]*?)\s*:(?![=])", repo.read(rel), re.M):
            targets |= {t for t in m[1].split() if not t.startswith(".")}
    return targets


def iter_lines(text: str):
    """(line number, line, inside a fenced code block)."""
    fenced = False
    for i, line in enumerate(text.split("\n"), 1):
        if line.lstrip().startswith("```"):
            fenced = not fenced
            continue
        yield i, line, fenced


def make_target_of(command: str) -> str | None:
    for token in command.split()[1:]:
        if "=" in token or token.startswith("-"):
            continue
        return token.rstrip(".,;:")
    return None


def check_make_references(repo: Repo) -> list[Finding]:
    targets = make_targets_defined(repo)
    findings = []
    for rel in repo.checked_documents():
        for i, line, fenced in iter_lines(repo.read(rel)):
            commands = [line.strip()] if fenced and re.match(r"^\s*make(\s|$)", line) else []
            commands += re.findall(r"`(make(?:\s[^`]*)?)`", line) if not fenced else []
            for command in commands:
                target = make_target_of(command.split("#")[0])
                if target and target not in targets and re.fullmatch(r"[a-zA-Z0-9_-]+", target):
                    findings.append(Finding(rel, i, "make", f"`make {target}`: the Makefile has no target `{target}`",
                                            "fix the reference, or add the target"))
    return findings


PROVIDER_MODEL = r'\bModel\s*\{\s*get;\s*set;\s*\}\s*=\s*"([^"]+)"'


def configured_models(repo: Repo) -> tuple[dict[str, str], list[Finding]]:
    findings: list[Finding] = []
    anchors = {
        "chat": ("src/Maf.Lab.Plugins.Abstractions/ModelOptions.cs", r'\bChatModel\s*\{\s*get;\s*set;\s*\}\s*=\s*"([^"]+)"'),
        "dense": ("src/Maf.Lab.Retrieval/Configuration/Options.cs", r'\bDenseVector\s*\{\s*get;\s*set;\s*\}\s*=\s*"([^"]+)"'),
    }
    values: dict[str, str] = {}
    # A provider plugin's pinned model (introduce-provider-plugins): the default `Model` its lib/ declares, keyed by the
    # plugin's name. Found by glob, as the build finds plugin code, so this script names no plugin.
    for manifest in sorted((repo.root / "plugins").glob("*/plugin.toml")):
        try:
            data = tomllib.loads(manifest.read_text(encoding="utf-8"))
        except tomllib.TOMLDecodeError:
            continue
        if data.get("kind") != "provider" or not data.get("name"):
            continue
        sources = sorted((manifest.parent / "lib").glob("*.cs"))
        pins = [m[1] for f in sources if (m := re.search(PROVIDER_MODEL, f.read_text(encoding="utf-8")))]
        if sources and not pins and data.get("provides") == "decision-engine":
            findings.append(Finding(str(manifest.parent.relative_to(repo.root)) + "/lib", None, "models",
                                    f"cannot find the configured {data['name']} value",
                                    "declare the pinned model as a `Model` property default in the provider's lib/"))
        elif pins:
            values[data["name"]] = pins[0]
    for kind, (rel, pattern) in anchors.items():
        m = re.search(pattern, repo.read(rel)) if repo.exists(rel) else None
        if not m:
            findings.append(Finding(rel, None, "models", f"cannot find the configured {kind} value",
                                    "update the anchor in scripts/docs.py to where the default now lives"))
            continue
        values[kind] = m[1]
    if "dense" in values:
        rel = anchors["dense"][0]
        m = re.search(r'\["' + re.escape(values["dense"]) + r'"\]\s*=\s*new\s+EmbeddingProfile\s*\{[^}]*?\bModel\s*=\s*"([^"]+)"',
                      repo.read("src/Maf.Lab.Plugins.Abstractions/ModelOptions.cs"), re.S)
        if m:
            values["embedding"] = m[1]
        else:
            findings.append(Finding(rel, None, "models", f"cannot find the embedding profile `{values['dense']}`",
                                    "update the anchor in scripts/docs.py"))
    if "chat" in values and (m := re.search(r"^CHAT_MODEL\s*\?=\s*(\S+)", repo.read("Makefile"), re.M)):
        if m[1] != values["chat"]:
            findings.append(Finding("Makefile", None, "models",
                                    f"CHAT_MODEL is `{m[1]}` but the code default is `{values['chat']}`",
                                    "make the two agree"))
    values.pop("dense", None)
    return values, findings


def check_models(repo: Repo) -> list[Finding]:
    values, findings = configured_models(repo)
    patterns = repo.config.get("models", {}).get("patterns", {})
    allowed = repo.config.get("models", {}).get("allowed", {})
    seen_allowed = set()
    for key, reason in allowed.items():
        if not str(reason).strip():
            findings.append(Finding(CONFIG, None, "models", f"allowed model `{key}` has no reason", "state why"))
    for rel in repo.checked_documents():
        for i, line in enumerate(repo.read(rel).split("\n"), 1):
            for kind, pattern in patterns.items():
                for m in re.finditer(pattern, line):
                    name = m[0]
                    if name in allowed:
                        seen_allowed.add(name)
                    elif kind in values and name != values[kind]:
                        findings.append(Finding(rel, i, "models",
                                                f"names {kind} model `{name}`, but the code configures `{values[kind]}`",
                                                f"write `{values[kind]}`, or list `{name}` under [models.allowed]"))
    for key in allowed:
        if key not in seen_allowed:
            findings.append(Finding(CONFIG, None, "models", f"[models.allowed] names `{key}`, which no document mentions",
                                    "remove the entry"))
    return findings


LINK = re.compile(r"!?\[[^\]]*\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)")
HTML_REF = re.compile(r"\b(?:src|href|srcset)=\"([^\"]+)\"")


def check_links(repo: Repo) -> list[Finding]:
    findings = []
    for rel in repo.checked_documents():
        base = (repo.root / rel).parent
        for i, line, fenced in iter_lines(repo.read(rel)):
            if fenced:
                continue
            line = re.sub(r"`[^`]*`", "", line)
            targets = LINK.findall(line) + [t.split()[0] for t in HTML_REF.findall(line)]
            for target in targets:
                if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith("#"):
                    continue
                path = urllib.parse.unquote(target.split("#")[0].split("?")[0])
                resolved = repo.root / path.lstrip("/") if path.startswith("/") else base / path
                if not resolved.exists():
                    findings.append(Finding(rel, i, "links", f"`{target}` does not exist", "fix the path"))
    return findings


# ── pages ──────────────────────────────────────────────────────────────────────────────────────────────────────────

WEB_ROUTES = "web/src/App.tsx"
# `<Route path="…" element={<X` across lines; X = Navigate is a redirect. `index` routes carry no path and do not match.
PAGE_ROUTE = re.compile(r"<Route\s+path=\"([^\"]+)\"\s+element=\{\s*<([A-Za-z0-9_.]+)")


def web_pages(repo: Repo) -> list[tuple[str, int]]:
    """(path, line) of every page the web app registers: not redirects, not the catch-all, parameter segments dropped."""
    if not repo.exists(WEB_ROUTES):
        return []
    text = repo.read(WEB_ROUTES)
    pages = []
    for m in PAGE_ROUTE.finditer(text):
        path, element = m[1], m[2]
        if element == "Navigate" or path.strip("/") in ("*", ""):
            continue
        segments = [s for s in path.strip("/").split("/") if s and not s.startswith(":") and s != "*"]
        if segments:
            pages.append(("/" + "/".join(segments), text.count("\n", 0, m.start(1)) + 1))
    return pages


def check_pages(repo: Repo) -> list[Finding]:
    if not repo.exists("README.md"):
        return []
    readme = repo.read("README.md")
    return [Finding(WEB_ROUTES, line, "pages", f"page `{path}` is not named in README.md",
                    f"name it in README.md as `{path}`")
            for path, line in web_pages(repo) if f"`{path}`" not in readme]


def check_change_proposals(repo: Repo) -> list[Finding]:
    findings = []
    changes = repo.root / "openspec" / "changes"
    if not changes.is_dir():
        return findings
    for change in sorted(p for p in changes.iterdir() if p.is_dir() and p.name != "archive"):
        proposal = change / "proposal.md"
        rel = str(proposal.relative_to(repo.root))
        if not proposal.is_file():
            continue
        text = proposal.read_text(encoding="utf-8")
        m = re.search(r"^## Documentation impact[ \t]*\n(.*?)(?=^## |\Z)", text, re.M | re.S)
        if not m:
            findings.append(Finding(rel, None, "documentation-impact", f"change `{change.name}` has no "
                                    "`## Documentation impact` section",
                                    "name each document the change affects, or say why none is"))
        elif not re.sub(r"<!--.*?-->", "", m[1], flags=re.S).strip():
            findings.append(Finding(rel, None, "documentation-impact", "the `## Documentation impact` section is empty",
                                    "name each document the change affects, or say why none is"))
        findings += stopping_findings(rel, change.name, text)
        findings += progress_findings(rel, change.name, text)
        findings += principles_findings(rel, change.name, text)
    return findings


def proposal_section(text: str, title: str) -> str | None:
    """A proposal's `## <title>` section, HTML comments dropped; None when the proposal has no such section."""
    m = re.search(rf"^## {re.escape(title)}[ \t]*\n(.*?)(?=^## |\Z)", text, re.M | re.S)
    return None if not m else re.sub(r"<!--.*?-->", "", m[1], flags=re.S)


# Lines that answer for the whole section rather than state one fact: `None — <reason>`, `Not yet — <reason>`.
VERDICTS = ("Not yet", "None")


def labelled(body: str, labels: tuple[str, ...]) -> dict[str, str]:
    """
    The labelled lines of a section, as `{label: value}`: `Key: Esc`, `- Key: Esc` or `- **Key:** Esc`, a value going on
    over indented lines below it. A verdict (`None`, `Not yet`) counts only as the section's first labelled line.
    """
    names = "|".join(re.escape(label) for label in sorted(labels, key=len, reverse=True))
    pattern = re.compile(rf"^\s*(?:[-*]\s+)?(?:\*\*)?({names})(?:\*\*)?(?:\s*(?::|—|-)\s*|\s*$)(?:\*\*)?\s*(.*)$")
    found: dict[str, str] = {}
    current = None
    for line in body.splitlines():
        label = pattern.match(line)
        if label and (label[1] not in VERDICTS or not found):
            current = label[1]
            found[current] = label[2].strip()
        elif current and line.startswith((" ", "\t")) and line.strip():
            found[current] = f"{found[current]} {line.strip()}".strip()
    return found


# The four facts a proposal states about how what it adds is stopped (stop-anything), in the order they are asked for.
STOPPING_FACTS = ("Key", "Stop", "Recorded in", "Shown")
STOPPING_FIX = ("write `None — <reason>`, or one line each for `Key:`, `Stop:`, `Recorded in:` and `Shown:` "
                "(stop-anything)")


def stopping_findings(rel: str, change: str, text: str) -> list[Finding]:
    """
    A proposal's `## Stopping` section: either `None` with a reason, or the four facts, each with a value. What the
    section says is review's to judge; that it says it is checked here.
    """
    body = proposal_section(text, "Stopping")
    if body is None:
        return [Finding(rel, None, "stopping", f"change `{change}` has no `## Stopping` section", STOPPING_FIX)]
    if not body.strip():
        return [Finding(rel, None, "stopping", "the `## Stopping` section is empty", STOPPING_FIX)]
    found = labelled(body, ("None", *STOPPING_FACTS))
    if "None" in found:
        if not found["None"]:
            return [Finding(rel, None, "stopping", "the `## Stopping` section says `None` without a reason",
                            "say why: `None — <reason>`")]
        return []
    problems = []
    for fact in STOPPING_FACTS:
        if fact not in found:
            problems.append(Finding(rel, None, "stopping", f"the `## Stopping` section of `{change}` has no `{fact}:`",
                                    STOPPING_FIX))
        elif not found[fact]:
            problems.append(Finding(rel, None, "stopping", f"the `## Stopping` section of `{change}` has an empty `{fact}:`",
                                    STOPPING_FIX))
    return problems


# Where what a proposal adds shows its progress (progress-feedback): a terminal's bar, a page's themed progress, or both.
PROGRESS_FACTS = ("Terminal", "Page")
PROGRESS_FIX = ("write `None — <reason>`, `Terminal:` and/or `Page:` with how it shows, or "
                "`Not yet — <reason>; follow-up: <change>` (progress-feedback)")


def progress_findings(rel: str, change: str, text: str) -> list[Finding]:
    """
    A proposal's `## Progress` section: `None` with a reason; `Not yet` with a reason and the follow-up change that will
    meet the rule; or how it shows — at least one of `Terminal:` and `Page:`, every one given with a value.
    """
    body = proposal_section(text, "Progress")
    if body is None:
        return [Finding(rel, None, "progress", f"change `{change}` has no `## Progress` section", PROGRESS_FIX)]
    if not body.strip():
        return [Finding(rel, None, "progress", "the `## Progress` section is empty", PROGRESS_FIX)]
    found = labelled(body, (*VERDICTS, *PROGRESS_FACTS))
    if "None" in found:
        return [] if found["None"] else [Finding(rel, None, "progress",
                                                 "the `## Progress` section says `None` without a reason",
                                                 "say why: `None — <reason>`")]
    if "Not yet" in found:
        reason = re.split(r"[;,]?\s*follow-up\s*:", found["Not yet"], flags=re.I)[0].strip()
        follow_up = re.search(r"follow-up\s*:\s*\S", found["Not yet"], re.I)
        problems = []
        if not reason:
            problems.append(Finding(rel, None, "progress", "the `## Progress` section says `Not yet` without a reason",
                                    "say why: `Not yet — <reason>; follow-up: <change>`"))
        if not follow_up:
            problems.append(Finding(rel, None, "progress",
                                    "the `## Progress` section says `Not yet` without naming its follow-up",
                                    "name the change that will meet the rule: `…; follow-up: <change>`"))
        return problems
    given = [fact for fact in PROGRESS_FACTS if fact in found]
    if not given:
        return [Finding(rel, None, "progress", f"the `## Progress` section of `{change}` says neither `Terminal:` nor "
                        "`Page:`", PROGRESS_FIX)]
    return [Finding(rel, None, "progress", f"the `## Progress` section of `{change}` has an empty `{fact}:`", PROGRESS_FIX)
            for fact in given if not found[fact]]


# What a proposal stands on (solid-and-standards): how it keeps SOLID, and the established standards or patterns it uses.
PRINCIPLES_FACTS = ("SOLID", "Standards")
PRINCIPLES_FIX = ("write `None — <reason>`, or `SOLID:` and `Standards:` each with a value; anything of the project's own "
                  "as `Own: <what> — <why no standard fits>; DECISIONS §<n>` (solid-and-standards)")
OWN_LINE = re.compile(r"^\s*(?:[-*]\s+)?(?:\*\*)?Own(?:\*\*)?\s*(?::|—|-)\s*(?:\*\*)?\s*(.*)$")


def principles_findings(rel: str, change: str, text: str) -> list[Finding]:
    """
    A proposal's `## Principles` section: `None` with a reason, or `SOLID:` and `Standards:` each with a value. Every
    `Own:` line (a format, protocol or mechanism of the project's own) names the DECISIONS.md section that records why
    no established one fits. Whether the answer is good is review's to judge; that it is given is checked here.
    """
    body = proposal_section(text, "Principles")
    if body is None:
        return [Finding(rel, None, "principles", f"change `{change}` has no `## Principles` section", PRINCIPLES_FIX)]
    if not body.strip():
        return [Finding(rel, None, "principles", "the `## Principles` section is empty", PRINCIPLES_FIX)]
    found = labelled(body, ("None", *PRINCIPLES_FACTS))
    if "None" in found:
        return [] if found["None"] else [Finding(rel, None, "principles",
                                                 "the `## Principles` section says `None` without a reason",
                                                 "say why: `None — <reason>`")]
    problems = []
    for fact in PRINCIPLES_FACTS:
        if fact not in found:
            problems.append(Finding(rel, None, "principles",
                                    f"the `## Principles` section of `{change}` has no `{fact}:`", PRINCIPLES_FIX))
        elif not found[fact]:
            problems.append(Finding(rel, None, "principles",
                                    f"the `## Principles` section of `{change}` has an empty `{fact}:`", PRINCIPLES_FIX))
    for own in own_entries(body):
        if not re.search(r"DECISIONS(?:\.md)?\s*§\s*\d+", own):
            problems.append(Finding(rel, None, "principles",
                                    f"an `Own:` line of `{change}` names no DECISIONS section: {own or '(empty)'}",
                                    "record why no established standard fits and add `; DECISIONS §<n>`"))
    return problems


def own_entries(body: str) -> list[str]:
    """Every `Own:` entry of a section, each with the indented lines that continue it."""
    entries: list[str] = []
    current = False
    for line in body.splitlines():
        own = OWN_LINE.match(line)
        if own:
            entries.append(own[1].strip())
            current = True
        elif current and line.startswith((" ", "\t")) and line.strip():
            entries[-1] = f"{entries[-1]} {line.strip()}".strip()
        else:
            current = False
    return entries


# ── commands ───────────────────────────────────────────────────────────────────────────────────────────────────────

def generate(root: Path) -> int:
    repo = Repo(root)
    rendered, findings = render(repo)
    changed = []
    for rel, text in rendered.items():
        path = root / rel
        if path.read_text(encoding="utf-8") != text:
            path.write_text(text, encoding="utf-8")
            changed.append(rel)
    for f in findings:
        print(f, file=sys.stderr)
    print(f"docs: {len(changed)} file(s) rewritten" + (f": {', '.join(changed)}" if changed else ""))
    return 1 if findings else 0


def check(root: Path) -> int:
    on_disk = Repo(root)
    repo = Repo(root)
    rendered, findings = render(repo)
    blocks = 0
    for rel, text in rendered.items():
        current = on_disk.read(rel)
        found, _ = find_blocks(rel, current)
        blocks += len(found)
        if current != text:
            expected, _ = find_blocks(rel, text)
            cur_lines, exp_lines = current.split("\n"), text.split("\n")
            for (name, s, e), (_, s2, e2) in zip(found, expected):
                if cur_lines[s:e + 1] != exp_lines[s2:e2 + 1]:
                    findings.append(Finding(rel, s + 1, "generated-block",
                                            f"block `{name}` differs from its source", "run make docs"))
    route_findings, route_count = check_routes(on_disk)
    findings += route_findings
    findings += check_make_references(on_disk)
    findings += check_models(on_disk)
    findings += check_links(on_disk)
    findings += check_pages(on_disk)
    findings += check_change_proposals(on_disk)
    findings += check_plugin_manifests(on_disk)
    for f in findings:
        print(f)
    documents = len(on_disk.checked_documents())
    if findings:
        print(f"docs-check: {len(findings)} finding(s)")
        return 1
    print(f"docs-check: {documents} documents, {blocks} generated blocks, {route_count} routes, "
          f"{len(web_pages(on_disk))} pages — in sync")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("command", choices=("generate", "check"),
                        help="generate: rewrite generated blocks (make docs); check: verify, change nothing (make docs-check)")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args(argv)
    return generate(args.root) if args.command == "generate" else check(args.root)


if __name__ == "__main__":
    sys.exit(main())
