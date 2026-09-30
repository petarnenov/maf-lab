# Proposal

## Why

Changes follow OpenSpec, yet the documents outside `openspec/specs/` drift from the code. OpenSpec syncs only the main
specs. Nothing compares README, `docs/`, `openspec/project.md`, `openspec/config.yaml` or the agent instructions with
the code. A docs task appears in `tasks.md` only when the author remembers one: 10 of the last 20 archived changes have
none. Meanwhile three endpoints shipped without a row in `docs/http-api.md`. The same fact is also copied into up to five places. Today, on `main`:

- `openspec/project.md` (and its copy in `config.yaml`) leaves `Maf.Lab.A2A`, `Maf.Lab.ComplianceAgent` and
  `Maf.Lab.Hosting` out of the layout. It still names `nomic-embed-text` as the embedding model, and it lists compose
  services that are no longer the whole stack.
- `.github/copilot-instructions.md` routes only `/mcp` to an MCP server. It leaves out `/portfolio/mcp`, `/code/mcp`,
  `/v1/traces` and `/jaeger`.
- `docs/http-api.md` leaves out `GET /api/admin/intent-stats`, `GET /api/admin/jev-stats` and `POST /api/code/snippets`.
- README never mentions 20 of the Makefile's documented targets.

## What Changes

- **One source per fact, generated copies.** A fact that code or config already holds is written into the docs by a
  generator, between `generated` markers, and nowhere else by hand:
  - the Makefile target catalogue (the `##` comments), into README;
  - the repository layout (each project's `<Description>`), into `openspec/project.md`;
  - the load-balancer routing (`compose/lb/nginx.conf`), into README and the Copilot instructions;
  - the OpenSpec `context:` in `config.yaml`, from `openspec/project.md`. This retires the "keep the two in sync" comment.
- **`make docs` and `make docs-check`.** `make docs` rewrites every generated block. `make docs-check` changes nothing.
  It fails when:
  - a generated block differs from what `make docs` would write;
  - an API route has no row in `docs/http-api.md`, or a documented row has no route;
  - a doc references a `make` target that does not exist;
  - a doc names a chat, embedding or Jev model other than the one the code configures;
  - a relative markdown link points to a missing file;
  - an active change's proposal lacks a Documentation impact section.
- **Wired into CI.** `make docs-check` becomes part of `make ci` and of the push workflow's `specs` job. A pull request
  that leaves a doc behind the code fails.
- **Docs become part of every change.** `openspec/config.yaml` gains:
  - a proposal rule: a `Documentation impact` section that names each affected document, or says why none is;
  - a tasks rule: a closing task group that updates those documents and runs `make docs-check`;
  - archive guidance: `make docs-check` passes before archiving, then an advisory, read-only review lists prose that
    the change may have made stale. The review does not block the archive.
- **One-off cleanup** of the drift listed under Why. The check starts green.

## Capabilities

### New Capabilities
- `documentation-sync`: which facts are generated and from where, what `make docs-check` verifies, and the
  Documentation impact rule for changes.

### Modified Capabilities
- `make-workflow`: the target catalogue gains `docs` and `docs-check`.
- `continuous-integration`: the push workflow and `make ci` run `make docs-check`.

## Documentation impact

- `README.md`: the command list becomes a generated block with every documented target; the load-balancer routing
  becomes a generated table. A short "Keeping docs in sync" note explains `make docs`.
- `openspec/project.md`: the layout becomes a generated block. The embedding model and compose service list are
  corrected by hand, since they are prose.
- `openspec/config.yaml`: `context:` becomes generated. New `rules` and `operations.archive` entries.
- `.github/copilot-instructions.md`: the routing model becomes the generated table.
- `docs/http-api.md`: rows for the three undocumented endpoints.
- `CLAUDE.md`: `make docs` / `make docs-check` added to the command list.
- `DECISIONS.md`: a note on why generation plus check was chosen over generating the whole of `http-api.md`.

## Impact

- New: `scripts/docs.py` (Python 3, standard library only, like `scripts/corpus_stats.py`), with its unit tests under
  `scripts/tests/`.
- `Makefile`: two targets; `ci` depends on `docs-check`.
- `.github/workflows/ci.yml`: one step in the `specs` job.
- `src/*/*.csproj`, `tools/*/*.csproj`: each gains a one-line `<Description>`. No runtime code changes.
- No API, behavior, package or model change.
