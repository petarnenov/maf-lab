"""Unit tests for scripts/docs.py over small fixture trees. Run: python3 -m unittest discover -s scripts/tests"""
from __future__ import annotations

import contextlib
import io
import shutil
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import docs  # noqa: E402

MAKEFILE = """\
CHAT_MODEL ?= gpt-oss:120b
.PHONY: all up

all: up ## Start everything
up: ## Build and start the stack
hidden:
\t@true
"""

OPTIONS = """\
public sealed class ModelOptions
{
    public string ChatModel { get; set; } = "gpt-oss:120b";
    public Dictionary<string, EmbeddingProfile> Embeddings { get; set; } = new()
    {
        ["dense_v3"] = new EmbeddingProfile
        {
            Model = "embeddinggemma",
        },
    };
}
public sealed class RetrievalOptions
{
    public string DenseVector { get; set; } = "dense_v3";
}
"""

JEV = 'public sealed class JevOptions { public string Model { get; set; } = "jev-1.13.0"; }\n'

NGINX = """\
http {
    upstream api_pool {
        server api:8080 max_fails=1;
    }
    upstream code_pool {
        server mcp-code:8080;
    }
    server {
        location = /lb-health {
            return 200 'ok';
        }
        location /api/ {
            proxy_pass http://api_pool;
        }
        location = /code/mcp {
            proxy_pass http://code_pool/mcp;
        }
    }
}
"""

ENDPOINTS = """\
public static class ChatEndpoints
{
    public const string MePath = "/me";
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet(MePath, () => 1);
        api.MapPost("/chat/{runId}/stop", () => 1);
        // api.MapGet("/commented-out", () => 1);
        app.MapGet("/health", () => 1);
    }
}
"""

HTTP_API = """\
# API

| Method | Path | Body |
|---|---|---|
| GET | `/api/me` | — |
| POST | `/api/chat/{runId}/stop` | — |

## Plugin routes

<!-- generated:plugin-routes — edit a plugin's docs/http-api.md, then run make docs -->
<!-- /generated:plugin-routes -->
"""

PROJECT_MD = """\
# maf-lab

## Repository layout

<!-- generated:repo-layout — edit the source, then run make docs -->
<!-- /generated:repo-layout -->

Models: embeddinggemma.
"""

CONFIG_YAML = """\
schema: spec-driven

# generated:project-context — edit openspec/project.md, then run make docs
# /generated:project-context

rules:
  proposal:
    - keep me
"""

README = """\
# maf-lab

<!-- generated:make-targets — edit the Makefile's ## comments, then run make docs -->
<!-- /generated:make-targets -->

<!-- generated:lb-routes — edit compose/lb/nginx.conf, then run make docs -->
<!-- /generated:lb-routes -->

<!-- generated:plugins — edit plugins/<name>/plugin.toml, then run make docs -->
<!-- /generated:plugins -->

Run `make up` and make sure it works.
"""

COPILOT = """\
# Copilot

<!-- generated:lb-routes — edit compose/lb/nginx.conf, then run make docs -->
<!-- /generated:lb-routes -->
"""

TOML = """\
[layout]
".github" = "Workflows"
"compose" = "Compose files"
"docs" = "References"
"openspec" = "Specs"
"src" = ".NET projects"
"tools" = "Tools"
"tools/screens" = "Screenshot script"

[routes.undocumented]
"GET /health" = "infrastructure"

[routes.library]

[models.patterns]
chat = 'gpt-oss:\\d+b|qwen3:\\d+b'
embedding = 'nomic-embed-text|embeddinggemma'
jev = 'jev-\\d+\\.\\d+\\.\\d+'

[models.allowed]
"""

PROPOSAL = """\
# Proposal

## Why

Because.

## Progress

None — a fixture: it runs nothing long.

## Stopping

None — a fixture: it starts nothing.

## Principles

None — a fixture: it designs nothing.

## Documentation impact

- README.md: a row.
"""


class Fixture:
    def __init__(self):
        self._dir = tempfile.TemporaryDirectory()
        self.root = Path(self._dir.name)
        files = {
            "Makefile": MAKEFILE,
            "README.md": README,
            "CLAUDE.md": "# CLAUDE\n",
            ".github/copilot-instructions.md": COPILOT,
            "compose/lb/nginx.conf": NGINX,
            "docs/docs-sync.toml": TOML,
            "docs/http-api.md": HTTP_API,
            "openspec/project.md": PROJECT_MD,
            "openspec/config.yaml": CONFIG_YAML,
            "openspec/changes/add-thing/proposal.md": PROPOSAL,
            "openspec/changes/archive/2026-01-01-old/proposal.md": "# Proposal\n",
            "src/Maf.Lab.Api/Maf.Lab.Api.csproj": "<Project><PropertyGroup><Description>The agent host</Description>"
                                                  "</PropertyGroup></Project>\n",
            "src/Maf.Lab.Api/Endpoints/ChatEndpoints.cs": ENDPOINTS,
            "src/Maf.Lab.Retrieval/Maf.Lab.Retrieval.csproj": "<Project><PropertyGroup><Description>MCP server\n"
                                                              "  over Qdrant</Description></PropertyGroup></Project>\n",
            "src/Maf.Lab.Retrieval/Configuration/Options.cs": OPTIONS,
            "src/Maf.Lab.Retrieval/Jev/JevOptions.cs": JEV,
            "tools/screens/capture.mjs": "// capture\n",
        }
        for rel, text in files.items():
            self.write(rel, text)

    def write(self, rel: str, text: str) -> None:
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def read(self, rel: str) -> str:
        return (self.root / rel).read_text(encoding="utf-8")

    def edit(self, rel: str, old: str, new: str) -> None:
        text = self.read(rel)
        assert old in text, f"{old!r} not in {rel}"
        self.write(rel, text.replace(old, new))

    def run(self, command: str) -> tuple[int, str]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = docs.main([command, "--root", str(self.root)])
        return code, out.getvalue()

    def close(self):
        self._dir.cleanup()


class DocsTestCase(unittest.TestCase):
    def setUp(self):
        self.fx = Fixture()
        self.addCleanup(self.fx.close)

    def generated(self) -> None:
        code, out = self.fx.run("generate")
        self.assertEqual(code, 0, out)

    def assertCheckFails(self, *fragments: str) -> str:
        code, out = self.fx.run("check")
        self.assertEqual(code, 1, out)
        for fragment in fragments:
            self.assertIn(fragment, out)
        return out

    def assertCheckPasses(self) -> str:
        code, out = self.fx.run("check")
        self.assertEqual(code, 0, out)
        return out


class GenerateTests(DocsTestCase):
    def test_make_targets_block_lists_documented_targets_only(self):
        self.generated()
        readme = self.fx.read("README.md")
        self.assertIn("| `make all` | Start everything |", readme)
        self.assertIn("| `make up` | Build and start the stack |", readme)
        self.assertNotIn("hidden", readme)
        self.assertIn("Run `make up` and make sure it works.", readme)

    def test_repo_layout_uses_csproj_descriptions_and_the_toml(self):
        self.generated()
        project = self.fx.read("openspec/project.md")
        self.assertIn("Maf.Lab.Api/", project)
        self.assertIn("The agent host", project)
        self.assertIn("MCP server over Qdrant", project)
        self.assertIn("screens/", project)
        self.assertIn("Screenshot script", project)
        self.assertIn("Models: embeddinggemma.", project)

    def test_lb_routes_names_services_exact_and_prefix(self):
        self.generated()
        for rel in ("README.md", ".github/copilot-instructions.md"):
            text = self.fx.read(rel)
            self.assertIn("| `/lb-health` | exact | the balancer itself |", text)
            self.assertIn("| `/api/` | prefix | `api` |", text)
            self.assertIn("| `/code/mcp` | exact | `mcp-code` at `/mcp` |", text)

    def test_project_context_copies_the_generated_project_md(self):
        self.generated()
        config = self.fx.read("openspec/config.yaml")
        self.assertIn("context: |\n  # maf-lab\n", config)
        self.assertIn("  The agent host", config)
        self.assertIn("rules:\n  proposal:\n    - keep me", config)

    def test_generate_is_idempotent(self):
        self.generated()
        before = {rel: self.fx.read(rel) for rel in docs.GENERATED_FILES}
        code, out = self.fx.run("generate")
        self.assertEqual(code, 0, out)
        self.assertIn("0 file(s) rewritten", out)
        self.assertEqual(before, {rel: self.fx.read(rel) for rel in docs.GENERATED_FILES})

    def test_new_target_reaches_the_readme(self):
        self.generated()
        self.fx.edit("Makefile", "hidden:", "docs: ## Rewrite generated blocks\nhidden:")
        self.assertCheckFails("README.md", "block `make-targets` differs")
        self.generated()
        self.assertIn("| `make docs` | Rewrite generated blocks |", self.fx.read("README.md"))


class BlockTests(DocsTestCase):
    def test_clean_tree_passes_with_summary(self):
        self.generated()
        out = self.assertCheckPasses()
        self.assertIn("generated blocks", out)
        self.assertIn("in sync", out)

    def test_check_changes_nothing(self):
        before = self.fx.read("README.md")
        self.fx.run("check")
        self.assertEqual(before, self.fx.read("README.md"))

    def test_hand_edit_inside_a_block_fails(self):
        self.generated()
        self.fx.edit("README.md", "| Build and start the stack |", "| Starts it |")
        self.assertCheckFails("README.md:", "block `make-targets` differs from its source", "run make docs")

    def test_missing_block_fails(self):
        self.fx.write(".github/copilot-instructions.md", "# Copilot\n")
        self.generated_ignoring_findings()
        self.assertCheckFails(".github/copilot-instructions.md", "block `lb-routes` is missing")

    def test_unbalanced_marker_fails(self):
        self.generated()
        # The last block of the file: an earlier one left open would be reported as a block opening inside it.
        self.fx.edit("README.md", "<!-- /generated:plugins -->", "")
        self.assertCheckFails("`plugins` is never closed")

    def generated_ignoring_findings(self):
        self.fx.run("generate")


class LayoutTests(DocsTestCase):
    def test_project_without_description_fails(self):
        self.fx.write("src/Maf.Lab.New/Maf.Lab.New.csproj", "<Project></Project>\n")
        self.fx.run("generate")
        self.assertCheckFails("src/Maf.Lab.New", "has no description", "<Description>")

    def test_top_level_directory_without_description_fails(self):
        self.fx.write("scratch/notes.txt", "x\n")
        self.fx.run("generate")
        self.assertCheckFails("top-level directory `scratch/` has no description")

    def test_stale_layout_entry_fails(self):
        self.fx.edit("docs/docs-sync.toml", '"tools/screens"', '"tools/gone" = "x"\n"tools/screens"')
        self.fx.run("generate")
        self.assertCheckFails("describes `tools/gone`")


class RouteTests(DocsTestCase):
    def setUp(self):
        super().setUp()
        self.generated()

    def test_grouped_and_const_routes_are_documented(self):
        self.assertCheckPasses()

    def test_a_health_checks_endpoint_is_a_get_route(self):
        # MapHealthChecks answers GET (extract-topology-plugin D1): /health stays registered, and so stays exempt.
        self.fx.edit("src/Maf.Lab.Api/Endpoints/ChatEndpoints.cs", 'app.MapGet("/health"', 'app.MapHealthChecks("/health"')
        self.assertCheckPasses()

    def test_undocumented_route_fails_at_its_registration(self):
        self.fx.edit("src/Maf.Lab.Api/Endpoints/ChatEndpoints.cs", 'app.MapGet("/health"',
                     'api.MapDelete("/conversations/{id}", () => 1);\n        app.MapGet("/health"')
        self.assertCheckFails("ChatEndpoints.cs:10: routes: DELETE /api/conversations/{} is registered but not in")

    def test_documented_route_without_registration_fails(self):
        self.fx.edit("docs/http-api.md", "| POST | `/api/chat/{runId}/stop` | — |",
                     "| POST | `/api/chat/{runId}/stop` | — |\n| GET | `/api/gone` | — |")
        self.assertCheckFails("docs/http-api.md:7: routes: GET /api/gone is documented but not registered")

    def test_escaped_pipe_inside_a_cell_keeps_the_row(self):
        self.fx.edit("docs/http-api.md", "| GET | `/api/me` | — |", "| GET | `/api/me?window=1h\\|6h` | — |")
        self.assertCheckPasses()

    def test_unresolvable_argument_fails_closed(self):
        self.fx.edit("src/Maf.Lab.Api/Endpoints/ChatEndpoints.cs", "api.MapGet(MePath", "api.MapGet(Paths.Build()")
        self.assertCheckFails("cannot resolve route argument `Paths.Build(")

    def test_commented_out_registration_is_ignored(self):
        out = self.assertCheckPasses()
        self.assertNotIn("commented-out", out)

    def test_library_family_row_is_accepted(self):
        self.fx.edit("docs/http-api.md", "| GET | `/api/me` | — |",
                     "| GET | `/api/me` | — |\n| POST/GET | `/a2a/message:send`, `/a2a/tasks…` | — |")
        self.fx.edit("docs/docs-sync.toml", "[routes.library]\n",
                     '[routes.library]\n"/a2a/message:send" = "SDK"\n"/a2a/tasks…" = "SDK"\n')
        self.assertCheckPasses()

    def test_stale_exemption_fails(self):
        self.fx.edit("docs/docs-sync.toml", '"GET /health" = "infrastructure"',
                     '"GET /health" = "infrastructure"\n"GET /gone" = "old"')
        self.assertCheckFails("[routes.undocumented] names `GET /gone`, which is not registered")

    def test_exemption_without_reason_fails(self):
        self.fx.edit("docs/docs-sync.toml", '"GET /health" = "infrastructure"', '"GET /health" = ""')
        self.assertCheckFails("exemption `GET /health` has no reason")


class MakeReferenceTests(DocsTestCase):
    def setUp(self):
        super().setUp()
        self.generated()

    def test_unknown_target_in_inline_code_fails(self):
        self.fx.write("docs/guide.md", "Run `make deploy` now.\n")
        self.assertCheckFails("docs/guide.md:1: make: `make deploy`")

    def test_unknown_target_in_fenced_block_fails(self):
        self.fx.write("docs/guide.md", "```bash\nmake CI_MODE=1 nope   # comment\n```\n")
        self.assertCheckFails("docs/guide.md:2: make: `make nope`")

    def test_prose_and_undocumented_targets_are_fine(self):
        self.fx.write("docs/guide.md", "Make sure to `make hidden` and `make` and `make up SUITE=x`.\n")
        self.assertCheckPasses()


class ModelTests(DocsTestCase):
    def setUp(self):
        super().setUp()
        self.generated()

    def test_stale_embedding_model_fails(self):
        self.fx.write("docs/guide.md", "Embeddings: nomic-embed-text.\n")
        self.assertCheckFails("docs/guide.md:1: models: names embedding model `nomic-embed-text`, "
                              "but the code configures `embeddinggemma`")

    def test_stale_jev_version_fails(self):
        self.fx.write("CLAUDE.md", "Jev `jev-1.12.0`.\n")
        self.assertCheckFails("CLAUDE.md:1: models: names jev model `jev-1.12.0`")

    def test_allowed_model_passes_and_unused_allowance_fails(self):
        self.fx.write("docs/guide.md", "Fallback qwen3:4b.\n")
        self.fx.edit("docs/docs-sync.toml", "[models.allowed]\n", '[models.allowed]\n"qwen3:4b" = "fallback"\n')
        self.assertCheckPasses()
        self.fx.write("docs/guide.md", "No fallback.\n")
        self.assertCheckFails("[models.allowed] names `qwen3:4b`")

    def test_history_is_not_checked(self):
        self.fx.write("DECISIONS.md", "We used nomic-embed-text and jev-1.0.0.\n")
        self.fx.write("openspec/changes/archive/2026-01-01-old/design.md", "nomic-embed-text\n")
        self.assertCheckPasses()

    def test_makefile_and_code_must_agree(self):
        self.fx.edit("Makefile", "CHAT_MODEL ?= gpt-oss:120b", "CHAT_MODEL ?= gpt-oss:20b")
        self.assertCheckFails("CHAT_MODEL is `gpt-oss:20b` but the code default is `gpt-oss:120b`")

    def test_moved_default_fails_loudly(self):
        self.fx.write("src/Maf.Lab.Retrieval/Jev/JevOptions.cs", "public sealed class JevOptions { }\n")
        self.assertCheckFails("cannot find the configured jev value")


class LinkTests(DocsTestCase):
    def setUp(self):
        super().setUp()
        self.generated()

    def test_broken_relative_link_fails(self):
        self.fx.write("docs/guide.md", "See [the API](http-api.md) and [gone](missing.md#top).\n")
        out = self.assertCheckFails("docs/guide.md:1: links: `missing.md#top` does not exist")
        self.assertNotIn("http-api.md` does not exist", out)

    def test_broken_image_and_html_source_fail(self):
        self.fx.write("docs/guide.md", '![shot](screenshots/a.png)\n<img src="../web/b.png">\n')
        self.assertCheckFails("`screenshots/a.png` does not exist", "`../web/b.png` does not exist")

    def test_external_anchor_and_code_links_are_ignored(self):
        self.fx.write("docs/guide.md", textwrap.dedent("""\
            [site](https://example.com) [top](#top) `[x](nope.md)`
            ```
            [y](nope.md)
            ```
            """))
        self.assertCheckPasses()


APP_TSX = """\
export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/chat" replace />} />
        <Route path="chat/:conversationId?" element={<ChatPage />} />
        <Route path="coverage" element={<CoveragePage />} />
        <Route
          path="admin/jev"
          element={
            <RequireAdmin>
              <JevPage />
            </RequireAdmin>
          }
        />
        <Route path="admin/intents" element={<Navigate to="/admin/jev" replace />} />
        <Route path="*" element={<Navigate to="/chat" replace />} />
      </Route>
    </Routes>
  );
}
"""


class PageTests(DocsTestCase):
    def setUp(self):
        super().setUp()
        self.generated()

    def with_app(self) -> None:
        self.fx.write("web/src/App.tsx", APP_TSX)
        self.fx.edit("docs/docs-sync.toml", '"tools" = "Tools"', '"tools" = "Tools"\n"web" = "Web app"')
        self.generated()

    def test_no_web_app_means_no_finding(self):
        self.assertCheckPasses()

    def test_every_named_page_passes(self):
        self.with_app()
        self.fx.edit("README.md", "Run `make up`", "Screens: `/chat`, `/coverage`, [`/admin/jev`](https://example.com/admin/jev). Run `make up`")
        out = self.assertCheckPasses()
        self.assertIn("3 pages", out)

    def test_unnamed_page_fails_at_its_route(self):
        self.with_app()
        self.fx.edit("README.md", "Run `make up`", "Screens: `/chat`, `/admin/jev`. Run `make up`")
        out = self.assertCheckFails("web/src/App.tsx:7: pages: page `/coverage` is not named in README.md")
        self.assertNotIn("`/chat`", out)

    def test_multiline_route_reports_its_path_line(self):
        self.with_app()
        self.fx.edit("README.md", "Run `make up`", "Screens: `/chat`, `/coverage`. Run `make up`")
        self.assertCheckFails("web/src/App.tsx:9: pages: page `/admin/jev`")

    def test_redirects_catch_all_and_parameters_are_not_pages(self):
        self.with_app()
        self.fx.edit("README.md", "Run `make up`", "Screens: `/chat`, `/coverage`, `/admin/jev`. Run `make up`")
        out = self.assertCheckPasses()
        self.assertNotIn("/admin/intents", out)

    def test_plain_prose_mention_is_not_enough(self):
        self.with_app()
        self.fx.edit("README.md", "Run `make up`", "Open /coverage, `/chat` and `/admin/jev`. Run `make up`")
        self.assertCheckFails("page `/coverage` is not named")


PRINCIPLED = "## Principles\n\nNone — it designs nothing.\n\n"
SHOWS_NOTHING = "## Progress\n\nNone — it runs nothing long.\n\n" + PRINCIPLED
STOPS_NOTHING = SHOWS_NOTHING + "## Stopping\n\nNone — it starts nothing.\n\n"
DOCUMENTED = "## Documentation impact\n\nNone: tests only.\n"


class ProposalTests(DocsTestCase):
    def setUp(self):
        super().setUp()
        self.generated()

    def propose(self, body: str) -> None:
        self.fx.write("openspec/changes/add-thing/proposal.md", "# Proposal\n\n" + body)

    def test_missing_section_fails(self):
        self.propose("## Why\n\nBecause.\n\n" + STOPS_NOTHING)
        self.assertCheckFails("change `add-thing` has no `## Documentation impact` section")

    def test_empty_section_fails(self):
        self.propose(STOPS_NOTHING + "## Documentation impact\n\n<!-- todo -->\n\n## Impact\n\nx\n")
        self.assertCheckFails("the `## Documentation impact` section is empty")

    def test_none_with_reason_passes(self):
        self.propose(STOPS_NOTHING + "## Documentation impact\n\nNone: tests only, no behavior a document describes.\n")
        self.assertCheckPasses()


class StoppingTests(DocsTestCase):
    """How a proposal says what it adds is stopped (stop-anything): `None — reason`, or the four facts."""

    def setUp(self):
        super().setUp()
        self.generated()

    def propose(self, body: str) -> None:
        # Every proposal here also says how it shows progress: that rule is ProgressTests'.
        self.fx.write("openspec/changes/add-thing/proposal.md", "# Proposal\n\n" + SHOWS_NOTHING + body)

    FOUR = ("## Stopping\n\n- Key: Esc on the index screen\n- Stop: POST /api/admin/jobs/{id}/cancel\n"
            "- Recorded in: the AdminJobs row, canceled atomically\n- Shown: \"Stopping…\", then canceled\n\n")

    def test_the_four_facts_pass(self):
        self.propose(self.FOUR + DOCUMENTED)
        self.assertCheckPasses()

    def test_bold_list_items_and_continuation_lines_pass(self):
        self.propose("## Stopping\n\n- **Key:** Esc\n- **Stop:** CopilotKit's stop\n- **Recorded in:**\n  the request itself\n"
                     "- **Shown:** \"Stopping…\", then \"Stopped.\"\n\n" + DOCUMENTED)
        self.assertCheckPasses()

    def test_none_with_a_reason_passes(self):
        self.propose("## Stopping\n\nNone — it only renames a document.\n\n" + DOCUMENTED)
        self.assertCheckPasses()

    def test_a_missing_section_fails(self):
        self.propose(DOCUMENTED)
        self.assertCheckFails("change `add-thing` has no `## Stopping` section", "stop-anything")

    def test_an_empty_section_fails(self):
        self.propose("## Stopping\n\n<!-- later -->\n\n" + DOCUMENTED)
        self.assertCheckFails("the `## Stopping` section is empty")

    def test_a_missing_fact_fails_by_name(self):
        self.propose(self.FOUR.replace("- Recorded in: the AdminJobs row, canceled atomically\n", "") + DOCUMENTED)
        out = self.assertCheckFails("has no `Recorded in:`")
        self.assertNotIn("has no `Key:`", out)

    def test_an_empty_fact_fails_by_name(self):
        self.propose(self.FOUR.replace('- Shown: "Stopping…", then canceled', "- Shown:") + DOCUMENTED)
        self.assertCheckFails("has an empty `Shown:`")

    def test_two_gaps_are_both_named(self):
        self.propose("## Stopping\n\n- Key: Esc\n- Stop: a cancel route\n\n" + DOCUMENTED)
        self.assertCheckFails("has no `Recorded in:`", "has no `Shown:`")

    def test_none_without_a_reason_fails(self):
        self.propose("## Stopping\n\nNone\n\n" + DOCUMENTED)
        self.assertCheckFails("says `None` without a reason")

    def test_an_archived_change_is_not_checked(self):
        self.fx.write("openspec/changes/archive/2026-01-01-old/proposal.md", "# Proposal\n\n" + DOCUMENTED)
        self.propose("## Stopping\n\nNone — it starts nothing.\n\n" + DOCUMENTED)
        self.assertCheckPasses()


class ProgressTests(DocsTestCase):
    """How a proposal says what it adds shows progress (progress-feedback): None, how it shows, or not yet."""

    STOPS = "## Stopping\n\nNone — it starts nothing.\n\n" + PRINCIPLED

    def setUp(self):
        super().setUp()
        self.generated()

    def propose(self, progress: str) -> None:
        # Every proposal here also says how it stops: that rule is StoppingTests'.
        self.fx.write("openspec/changes/add-thing/proposal.md",
                      "# Proposal\n\n## Progress\n\n" + progress + "\n\n" + self.STOPS + DOCUMENTED)

    def test_a_terminal_bar_alone_passes(self):
        self.propose("- Terminal: one bar over the documents, done/total")
        self.assertCheckPasses()

    def test_a_page_alone_passes(self):
        self.propose("- **Page:** themed progress naming the current step")
        self.assertCheckPasses()

    def test_both_pass(self):
        self.propose("- Terminal: a bar per suite\n- Page: the attempts as they finish")
        self.assertCheckPasses()

    def test_none_with_a_reason_passes(self):
        self.propose("None — it only renames a document.")
        self.assertCheckPasses()

    def test_not_yet_with_a_follow_up_passes(self):
        self.propose("Not yet — the runner reports no steps; follow-up: add-runner-steps")
        self.assertCheckPasses()

    def test_a_missing_section_fails(self):
        self.fx.write("openspec/changes/add-thing/proposal.md", "# Proposal\n\n" + self.STOPS + DOCUMENTED)
        self.assertCheckFails("change `add-thing` has no `## Progress` section", "progress-feedback")

    def test_an_empty_section_fails(self):
        self.propose("<!-- later -->")
        self.assertCheckFails("the `## Progress` section is empty")

    def test_an_empty_page_fails_by_name(self):
        self.propose("- Terminal: a bar\n- Page:")
        out = self.assertCheckFails("has an empty `Page:`")
        self.assertNotIn("has an empty `Terminal:`", out)

    def test_none_without_a_reason_fails(self):
        self.propose("None")
        self.assertCheckFails("says `None` without a reason")

    def test_not_yet_without_a_follow_up_fails(self):
        self.propose("Not yet — later")
        self.assertCheckFails("says `Not yet` without naming its follow-up")

    def test_not_yet_without_a_reason_fails(self):
        self.propose("Not yet — follow-up: add-runner-steps")
        self.assertCheckFails("says `Not yet` without a reason")

    def test_no_fact_at_all_fails(self):
        self.propose("It will show something.")
        self.assertCheckFails("says neither `Terminal:` nor `Page:`")

    def test_a_proposal_missing_both_rules_learns_both(self):
        self.fx.write("openspec/changes/add-thing/proposal.md", "# Proposal\n\n" + DOCUMENTED)
        self.assertCheckFails("has no `## Progress` section", "has no `## Stopping` section")


class PrinciplesTests(DocsTestCase):
    """What a proposal stands on (solid-and-standards): None, or SOLID and Standards; anything its own names a decision."""

    REST = "## Progress\n\nNone — it runs nothing long.\n\n## Stopping\n\nNone — it starts nothing.\n\n" + DOCUMENTED

    def setUp(self):
        super().setUp()
        self.generated()

    def propose(self, principles: str) -> None:
        # Every proposal here also says how it shows progress and stops: those rules are their own tests'.
        self.fx.write("openspec/changes/add-thing/proposal.md",
                      "# Proposal\n\n## Principles\n\n" + principles + "\n\n" + self.REST)

    BOTH = ("- SOLID: one responsibility per plugin; the core depends on abstractions only\n"
            "- Standards: microkernel (POSA), composition root, OAuth RFC 8707\n")

    def test_solid_and_standards_pass(self):
        self.propose(self.BOTH)
        self.assertCheckPasses()

    def test_bold_items_and_continuation_lines_pass(self):
        self.propose("- **SOLID:**\n  open/closed through extension points\n- **Standards:** MCP, A2A")
        self.assertCheckPasses()

    def test_none_with_a_reason_passes(self):
        self.propose("None — it only renames a document.")
        self.assertCheckPasses()

    def test_own_with_a_decision_passes(self):
        self.propose(self.BOTH + "- Own: plugin.toml — no standard holds scope and environments; DECISIONS §81")
        self.assertCheckPasses()

    def test_a_missing_section_fails(self):
        self.fx.write("openspec/changes/add-thing/proposal.md", "# Proposal\n\n" + self.REST)
        self.assertCheckFails("change `add-thing` has no `## Principles` section", "solid-and-standards")

    def test_an_empty_section_fails(self):
        self.propose("<!-- later -->")
        self.assertCheckFails("the `## Principles` section is empty")

    def test_a_missing_fact_fails_by_name(self):
        self.propose("- SOLID: single responsibility")
        out = self.assertCheckFails("has no `Standards:`")
        self.assertNotIn("has no `SOLID:`", out)

    def test_an_empty_fact_fails_by_name(self):
        self.propose("- SOLID: single responsibility\n- Standards:")
        self.assertCheckFails("has an empty `Standards:`")

    def test_none_without_a_reason_fails(self):
        self.propose("None")
        self.assertCheckFails("says `None` without a reason")

    def test_own_without_a_decision_fails(self):
        self.propose(self.BOTH + "- Own: a wire format of our own — it was quicker")
        self.assertCheckFails("an `Own:` line of `add-thing` names no DECISIONS section", "DECISIONS §<n>")

    def test_an_own_entry_may_go_on_over_indented_lines(self):
        self.propose(self.BOTH + "- Own: plugin.toml — no standard holds\n  scope and environments; DECISIONS §81")
        self.assertCheckPasses()

    def test_every_own_line_is_checked(self):
        self.propose(self.BOTH + "- Own: a — x; DECISIONS §3\n- Own: b — y")
        out = self.assertCheckFails("names no DECISIONS section: b — y")
        self.assertNotIn("section: a — x", out)


GOOD_MANIFEST = """\
schema = 1
name = "weather"
kind = "mcp"
scope = "tenant"
environments = ["dev", "qa"]
description = "Forecasts"
progress = "None — every call is short"
stopping = "None — no long work"
"""


class PluginTests(DocsTestCase):
    """Plugins in the docs (introduce-plugins task 3.6): manifests checked, their targets and routes in the tables."""

    def setUp(self):
        super().setUp()
        self.fx.edit("docs/docs-sync.toml", '"openspec" = "Specs"', '"openspec" = "Specs"\n"plugins" = "One folder per plugin"')
        self.fx.write("plugins/plugin.schema.json", (Path(__file__).resolve().parents[2] / "plugins" / "plugin.schema.json").read_text())

    def test_a_valid_manifest_is_listed_and_passes(self):
        self.fx.write("plugins/weather/plugin.toml", GOOD_MANIFEST)
        self.generated()
        self.assertIn("| `weather` | mcp | tenant | dev, qa | Forecasts |", self.fx.read("README.md"))
        self.assertCheckPasses()

    def test_a_manifest_without_stopping_fails_naming_the_plugin(self):
        self.fx.write("plugins/weather/plugin.toml", GOOD_MANIFEST.replace('stopping = "None — no long work"\n', ""))
        self.assertCheckFails("plugins/weather/plugin.toml", "missing required `stopping`")

    def test_a_stopping_without_its_facts_fails(self):
        self.fx.write("plugins/weather/plugin.toml", GOOD_MANIFEST.replace('"None — no long work"', '"Key: Esc"'))
        self.assertCheckFails("/stopping", "Stop:, Recorded in:, Shown:")

    def test_an_unknown_key_and_a_missing_dependency_fail(self):
        self.fx.write("plugins/weather/plugin.toml", GOOD_MANIFEST + 'depends = ["sun"]\ncolour = "blue"\n')
        self.assertCheckFails("unknown key `colour`", "depends on `sun`")

    def test_a_plugins_make_targets_and_routes_reach_the_tables(self):
        self.fx.write("plugins/weather/plugin.toml", GOOD_MANIFEST)
        self.fx.write("plugins/weather/plugin.mk", "weather-index: ## Index the forecasts\n\t@true\n")
        self.fx.write("plugins/weather/lb.http.conf", "upstream weather_pool {\n    server weather:8080;\n}\n")
        self.fx.write("plugins/weather/lb.server.conf",
                      "location = /weather/mcp {\n    proxy_pass http://weather_pool/mcp;\n}\n")
        self.generated()
        readme = self.fx.read("README.md")
        self.assertIn("| `make weather-index` | Index the forecasts |", readme)
        self.assertIn("| `/weather/mcp` | exact | `weather` at `/mcp` (plugin `weather`) |", readme)
        self.fx.edit("README.md", "Run `make up`", "Run `make weather-index` then `make up`")
        self.assertCheckPasses()

    def test_a_route_a_plugin_server_maps_must_be_documented(self):
        self.fx.write("plugins/weather/plugin.toml", GOOD_MANIFEST)
        self.fx.write("plugins/weather/server/WeatherPlugin.cs",
                      'public sealed class WeatherPlugin { void M(IEndpointRouteBuilder r) { r.MapGet("/api/weather", () => 1); } }\n')
        self.generated()
        self.assertCheckFails("GET /api/weather is registered but not in docs/http-api.md",
                              "to the plugin's docs/http-api.md")

    def test_a_plugin_documents_its_routes_in_its_folder_and_they_leave_with_it(self):
        self.fx.write("plugins/weather/plugin.toml", GOOD_MANIFEST)
        self.fx.write("plugins/weather/server/WeatherPlugin.cs",
                      'public sealed class WeatherPlugin { void M(IEndpointRouteBuilder r) { r.MapGet("/api/weather", () => 1); } }\n')
        self.fx.write("plugins/weather/docs/http-api.md",
                      "The forecast.\n\n| Method | Path | Body | Response |\n|---|---|---|---|\n| GET | `/api/weather` | — | `200` |\n")
        self.generated()
        api = self.fx.read("docs/http-api.md")
        self.assertIn("### weather\n\nThe forecast.", api)
        self.assertCheckPasses()

        shutil.rmtree(self.fx.root / "plugins" / "weather")
        self.generated()
        self.assertIn("No plugin present serves a route of its own.", self.fx.read("docs/http-api.md"))
        self.assertCheckPasses()


if __name__ == "__main__":
    unittest.main()
