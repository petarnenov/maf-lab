# Tasks

## 1. The script and its configuration

- [ ] 1.1 Create `docs/docs-sync.toml` with `[layout]` (one description per non-.NET top-level directory tracked by git),
  `[routes.undocumented]` (`GET /health` with its reason), `[routes.library]` (`POST /a2a` and the `/a2a/…` family,
  with reasons), `[models]` (the kind regexes from design.md) and `[models.allowed]` (`qwen3:4b` with its reason).
  Verify: `python3 -c "import tomllib; tomllib.load(open('docs/docs-sync.toml','rb'))"` succeeds.
- [ ] 1.2 Create `scripts/docs.py` with `generate` and `check` subcommands, the block-marker reader/writer for markdown
  and YAML, and the finding format `path:line: rule: message → fix`. It uses the standard library only.
  Verify: `python3 scripts/docs.py --help` lists both subcommands.
- [ ] 1.3 Implement the four generators (`make-targets`, `repo-layout`, `lb-routes`, `project-context`) in dependency
  order. Verify: unit tests in `scripts/tests/test_docs.py` on fixture trees show each block's exact output, and a
  second `generate` changes nothing.
- [ ] 1.4 Implement the route parser (groups, literals, `const string` resolution, parameter normalization, and
  unresolvable calls reported as findings) and the `http-api.md` row reader (method lists, comma-separated paths, `…`
  families). Verify: unit tests cover a grouped route, a const route, an unresolvable argument, an undocumented route,
  a documented route with no registration, a library family, and a stale exemption.
- [ ] 1.5 Implement the remaining rules: `make` references in code only, missing descriptions, model names against the
  configured values (including the Makefile `CHAT_MODEL` vs C# default), relative links and images, and the
  `Documentation impact` section in active proposals. Verify: one failing and one passing unit test per rule,
  including "make sure" in prose not being flagged and `DECISIONS.md` not being checked.
- [ ] 1.6 Add `docs`, `docs-check` and `require-python` to the Makefile (`docs-check` runs the unit tests first, then
  the check), with `##` descriptions, and add them to `.PHONY`. Verify: `make help` lists both, and `make docs-check`
  runs on a machine without Docker running.

## 2. Sources and markers (the one-off cleanup)

- [ ] 2.1 Add a one-line `<Description>` to every `src/*/*.csproj` and `tools/*/*.csproj`. Verify:
  `make lint-dotnet` still builds with warnings as errors, and the check reports no missing description.
- [ ] 2.2 Put `generated:make-targets` markers in README where the command list belongs, and replace the hand list.
  Put `generated:lb-routes` in README's architecture section and in `.github/copilot-instructions.md`, replacing the
  hand-written "Routing model" bullet. Verify: `make docs` fills all three, and README shows every `##` target,
  including the 20 that were missing.
- [ ] 2.3 Put `generated:repo-layout` around the layout in `openspec/project.md`, and `generated:project-context` around
  `context:` in `openspec/config.yaml`. Remove the "keep the two in sync" comment. Verify: after `make docs`, the layout
  lists `Maf.Lab.A2A`, `Maf.Lab.ComplianceAgent` and `Maf.Lab.Hosting`, `config.yaml`'s context equals `project.md`,
  and `openspec validate --all --strict` still passes.
- [ ] 2.4 Fix the prose in `openspec/project.md` by hand: the embedding model (`embeddinggemma`, a multilingual model,
  not `nomic-embed-text`) and the compose service list (all 14 services in `compose/docker-compose.yml` today, including
  `lb`, `redis`, `otel-collector`, `prometheus` and `jaeger`). Then run `make docs`. Verify: the
  model check passes, and every service in the compose file is named.
- [ ] 2.5 Add rows to `docs/http-api.md` for `GET /api/admin/intent-stats`, `GET /api/admin/jev-stats` (in Admin) and
  `POST /api/code/snippets` (in a Code section), with bodies and responses read from the endpoint code and the
  `intent-statistics` and `jev-statistics` specs. Fix the malformed Identity table header. Verify: the route rule
  passes in both directions.
- [ ] 2.6 Run `make docs-check` and fix every remaining finding (links, `make` references, models) at its source.
  Verify: `make docs-check` exits 0.

## 3. Process and CI

- [ ] 3.1 In `openspec/config.yaml`, add a `rules.proposal` entry (the `## Documentation impact` section), a `rules.tasks`
  entry (a closing docs task group that runs `make docs` and `make docs-check`), and `operations.archive.guidance` (run
  `make docs-check` and stop on failure, then the advisory read-only prose review). Verify:
  `openspec instructions proposal --change keep-docs-in-sync --json` shows the new rule, and
  `openspec instructions archive --change keep-docs-in-sync --json` shows the guidance.
- [ ] 3.2 Add `docs-check` to the `ci` target's prerequisites, and a `make docs-check` step to the `specs` job in
  `.github/workflows/ci.yml`. Verify: `make ci` runs it second, after `specs`, and the workflow YAML parses.
- [ ] 3.3 Prove the gate: on a scratch commit, add an undocumented `MapGet` and confirm that `make docs-check` fails
  with its method and path. Then revert. Verify: the failing output is quoted in the change notes, and the tree is
  clean afterwards.

## 4. Jev

- [ ] 4.1 Run the Jev review checklist (docs/rules/jev-usage.md §7) against this change. Record in the change that no Jev
  call is added or changed, so labeled-input tests (including Bulgarian ones) do not apply. Verify: the note exists in
  this file under 4.1.

## 5. Documentation

- [ ] 5.1 Add `make docs` and `make docs-check` to CLAUDE.md's command list, plus one non-negotiable: "Never edit
  inside a `generated:` block; edit its source and run `make docs`." Add the same line to
  `.github/copilot-instructions.md`. Verify: `make docs-check` passes, and both files mention it.
- [ ] 5.2 Add a short "Keeping docs in sync" section to README: what is generated from where, what the check verifies,
  and where exemptions live. Verify: the section's links resolve (`make docs-check`).
- [ ] 5.3 Add a DECISIONS.md entry: generation plus check over whole-document generation, static route parsing over
  booting the api, Python standard library over a new toolchain, and an advisory rather than gating semantic review.
  Verify: the entry exists and is dated 2026-09-30.
- [ ] 5.4 Run `make docs` and `make docs-check` a final time, then `make specs`. Verify: all three exit 0, and
  `git status` shows no diff from `make docs`.
