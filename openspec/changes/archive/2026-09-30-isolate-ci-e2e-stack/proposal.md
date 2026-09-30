# Proposal

## Why

After `make ci-e2e`, clicking any C# file on the Coverage screen shows "Could not load this file.". `ci-e2e` runs in
the dev stack's own compose project (`maf-lab`), so it also uses the dev stack's named volumes. Its test-generation run
accepts a candidate, and that promotes an official dotnet snapshot at a merge commit (`041553f…`). That commit exists
only in the throwaway clone `.cache/e2e-repo`. When the dev stack comes back on the real checkout, the snapshot is the
newest official snapshot for all 237 dotnet files. `GET /api/coverage/files` runs `git show <commit>:<path>`, which
fails, so the endpoint answers `404`. The web shows the same message for every kind of failure, so nothing on screen
says why.

## What Changes

- `make ci-e2e` runs in its own compose project (`maf-lab-e2e`), with its own volumes. Nothing it writes (the SQLite
  database, Qdrant, Redis, the test agent's work) reaches the dev stack's data. The ports are the same, so it first
  stops the dev stack with its volumes kept. It removes its own project and volumes when it passes, and leaves them
  running for inspection when it fails.
- The CI workflow's diagnostics step collects `ps` and logs from the `maf-lab-e2e` project.
- `GET /api/coverage/files` separates "no snapshot has this path" (`404`, unchanged) from "the snapshot's commit is not
  in this repository" (`409 source_unavailable { commit }`). A snapshot from another repository is then reported, not
  hidden behind a 404.
- The Coverage screen's file region shows a specific message for `source_unavailable`. It says the file was measured
  at a commit this repository does not have, gives the short commit, and tells the user to refresh coverage. The
  refresh is offered to administrators. Other failures keep the generic message.
- Operational fix for the checkout that is already affected: one `make coverage` adds newer official snapshots at
  `main`. The file view then works again, and no database surgery is needed.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `continuous-integration`: the model-free e2e runs in an isolated compose project and never writes into the local dev
  stack's data. Diagnostics come from that project.
- `coverage-dashboard`: the file view distinguishes a measurement whose commit is unavailable from other failures, and
  says what to do.

## Impact

- `Makefile` (`ci-e2e`, and a compose project variable), `.github/workflows/ci.yml` (diagnostics step).
- `src/Maf.Lab.Api/Endpoints/CoverageEndpoints.cs` (`/files` response when `ShowAsync` returns null), plus its
  tests.
- `web/src/coverage/CoveragePage.tsx` (the error message for `source_unavailable`), `web/src/api` error handling if
  the problem type is not surfaced yet, plus `CoveragePage.test.tsx`.
- No package, model, route or target is added or removed. Tenancy is untouched: coverage is repository-wide and has
  no firm.

## Documentation impact

- `docs/http-api.md`: the `/api/coverage/files` row gains `409 source_unavailable { commit }`.
- `README.md`: the `make ci-e2e` row is generated from the Makefile help text, so `make docs` regenerates it. The CI
  section notes that `make ci-e2e` runs in its own project, stops the dev stack, and that `make` brings it back.
- `CLAUDE.md`, `openspec/project.md`, `.github/copilot-instructions.md`: not affected. They do not describe how
  `ci-e2e` is isolated or the coverage error states.
