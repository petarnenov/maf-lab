# Tasks

## 1. Isolate the e2e stack

- [x] 1.1 Add `COMPOSE_PROJECT ?= maf-lab` to the Makefile and pass `-p $(COMPOSE_PROJECT)` in both `COMPOSE`
      definitions. Verify that `make ps` still lists the running dev stack and `make -n up` shows `-p maf-lab`.
- [x] 1.2 Rewrite `ci-e2e`: stop the dev project with its volumes kept (`docker compose -p maf-lab down
      --remove-orphans`, no `-v`), run the existing sub-make with `COMPOSE_PROJECT=maf-lab-e2e`, and on success
      `down -v --remove-orphans` the e2e project and print "run `make` to bring the dev stack back". On failure, print
      the project name and the `down -v` command, and exit non-zero. Keep the logic in `scripts/ci_e2e.sh` if it
      outgrows GNU Make 3.81. Verify with `make -n ci-e2e`.
- [x] 1.3 Add `-p maf-lab-e2e` to the `ps` and `logs` calls of the diagnostics step in `.github/workflows/ci.yml`.
      Verify by reading the workflow: every compose call in the e2e job names the project.

## 2. Say why a file cannot be shown

- [x] 2.1 In `CoverageEndpoints` `/files`, answer `409` problem `source_unavailable` with the extension `commit` and a
      detail that has only the short commit, when `ShowAsync` returns null. The two other `NotFound()` paths stay
      unchanged. Add cases to `tests/Maf.Lab.Tests/CoverageApiTests.cs`: a snapshot at an unknown commit gives
      409/`source_unavailable`/commit, and an unknown path is still 404. Verify with `make test-dotnet`.
- [x] 2.2 In `CoveragePage`, when the file query fails with `ApiError` status 409, show `error.message` and, for
      `FIRM_ADMIN`, the Refresh coverage control. Other failures keep "Could not load this file.". Add cases to
      `CoveragePage.test.tsx`: the 409 message with a refresh button for an admin, no button for a non-admin, and the
      generic message for a 500. Verify with `make test-web lint-web`.

## 3. Verify on the real stack

- [x] 3.1 Run `make ci-e2e` with the dev stack running. It passes, `docker compose ls` no longer lists `maf-lab-e2e`,
      and `docker volume ls` has no `maf-lab-e2e_*` volume.
- [x] 3.2 Run `make`, then check the dev database: no `CoverageSnapshots` row was added by the e2e run. That is, the
      newest official snapshot is not at a commit missing from the checkout.
- [x] 3.3 On the currently affected stack, before `make coverage`, clicking `RedisIdempotencyStore.cs` shows the
      "measured at a commit this repository does not have" message with the short commit `041553f5c412`. After
      `make coverage`, the file view loads its lines.

## 4. Documentation

- [x] 4.1 In `docs/http-api.md`, add `409 source_unavailable { commit }` to the `/api/coverage/files` row.
- [x] 4.2 In `README.md`'s CI section, say that `make ci-e2e` runs in its own compose project (`maf-lab-e2e`), stops the
      dev stack with its data kept, removes itself when it passes, and that `make` brings the dev stack back. Update
      the `ci-e2e` help text in the Makefile if it changes, and run `make docs` for the generated target table.
- [x] 4.3 Run `make docs-check` and `make specs`. Both pass.
