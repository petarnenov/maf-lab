# Design

## Context

See proposal.md, Why. The relevant facts:

- `compose/docker-compose.yml` sets `name: maf-lab`. Every `make` target, `ci-e2e` included, therefore addresses the
  same project and the same named volumes (`api-data` holds `/app-data/maf-lab.db`, plus the Qdrant, Redis and
  test-agent volumes). `ci-e2e` changes only `MAF_LAB_REPO` (to `.cache/e2e-repo`) and adds the CI overlay.
- The host ports are fixed (7171, 6333, 11435, …). Two stacks cannot run at once without a port scheme, and this
  change does not add one.
- `GET /api/coverage/files` returns `NotFound()` in three places: an unknown path, no snapshot, and
  `repository.ShowAsync(commit, path)` returning null. Only the third happened here.
- The web's `ApiError` carries `status` and a message: the problem's `detail`, or a fixed `'Not found.'` for 404.
  Coverage components already branch on `status === 409` (`CandidatePanel`, `RaiseThresholdDialog`).

## Goals / Non-Goals

**Goals:**
- A local `make ci-e2e` cannot change what the dev stack shows.
- When a file's measurement cannot be shown, the user is told why and what to do.

**Non-Goals:**
- Running the dev stack and the e2e stack at the same time (that needs a port scheme).
- Filtering snapshots with unavailable commits out of the tree, or falling back to an older snapshot. The numbers
  would disagree with the tree, and a refresh already heals it.
- Cleaning the already polluted dev database with a migration. One `make coverage` supersedes the bad snapshot. The
  accepted e2e run row for `E2eTarget.cs` stays in the history, which is harmless.

## Decisions

1. **Separate compose project, `-p maf-lab-e2e`.** The Makefile gains `COMPOSE_PROJECT ?= maf-lab`, passed to
   `$(COMPOSE)` as `-p $(COMPOSE_PROJECT)`. `ci-e2e` passes `COMPOSE_PROJECT=maf-lab-e2e` to its sub-make. Volumes
   are namespaced per project, so isolation comes from compose itself.
   *Alternatives:* (a) wipe the dev volumes after e2e: this destroys the developer's data, so no. (b) Point the e2e
   api at another SQLite path: this isolates only one of the stores that are written, so no.
2. **Stop the dev stack first, keep its volumes.** `ci-e2e` runs `docker compose -p maf-lab down --remove-orphans`
   without `-v` before it starts, because the ports collide. Today it already replaces the dev containers, so the dev
   stack is no less available than before. After the e2e project is gone, the output says to run `make` to bring the
   dev stack back. It does not restart the dev stack itself: `make` needs secrets that CI mode must not require.
3. **Cleanup on success, keep on failure.** The recipe runs the e2e sub-make. If it passes, it runs
   `docker compose -p maf-lab-e2e down -v --remove-orphans`. If it fails, it prints the project name and the
   `down -v` command, then exits non-zero. The CI diagnostics step then finds the containers running. The step adds
   `-p maf-lab-e2e` to its `ps` and `logs` calls.
4. **`409 source_unavailable` for a missing commit.** When `ShowAsync` returns null, the endpoint returns
   `Results.Problem(type: "source_unavailable", statusCode: 409, …)` with the extension `commit` and a `detail` like
   "This file was measured at commit 041553f5c412, which this repository does not have. Refresh coverage to measure
   it again." It uses 409 rather than 404: the path is known and measured, but the stored state conflicts with the
   repository. 409 is also what the coverage UI already treats as "show the server's detail". The detail contains
   only the short commit, with no host or filesystem path.
5. **Web: status 409 in the file region.** `CoveragePage` shows `error.message` when `file.error` is an `ApiError`
   with status 409, and `RefreshControl` for an administrator. Otherwise it keeps "Could not load this file.". The
   refresh invalidates `coverageKeys.all`, so the file refetches once the job finishes.

## Risks / Trade-offs

- [`ci-e2e` stops a dev stack the developer was using] → This is already the case today, when its containers are
  replaced. The message names `make` as the way back, and no data is lost.
- [A failed e2e leaves `maf-lab-e2e` holding the ports, and `make` then fails to bind 7171] → The failure output
  prints the exact `down -v` command. `make` itself is unchanged.
- [Docker images are shared between projects (same `image:` names)] → Only images, never data. Both stacks build
  from the same checkout, so a rebuild is at most extra work.

## Migration Plan

1. Land the change on `main`.
2. For the affected local stack, run `make coverage` once. The Coverage screen then loads files again.
3. Leftover containers from a previous `ci-e2e` belong to the `maf-lab` project and are replaced by the next `make`.
   Nothing else needs migrating.
