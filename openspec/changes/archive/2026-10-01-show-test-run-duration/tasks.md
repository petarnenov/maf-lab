# Tasks

## 1. Api: record when a run's work ended

- [x] 1.1 Add `TestGenRunState.Running` (submitted, working, verifying), a nullable `FinishedAt` on `TestGenRunRow`, and `TestGenRuns.StampFinish` called from `ApplyAsync` and `FinishAsync`; verify with xUnit tests (`RunDurationTests`, and the candidate lifecycle in `TestGenRunsApiTests`) that a candidate later accepted or discarded keeps its candidate time, a failure is stamped, and a working run has none
- [x] 1.2 Backfill `FinishedAt` in `DatabaseInitializer` from the run's first non-running event, else `UpdatedAt`, leaving running rows alone; verify with an xUnit test in `RunDurationTests` (a fresh database run through `DatabaseInitializer`)

## 2. Api: duration in the overview

- [x] 2.1 Add `StartedAt`, `FinishedAt`, `DurationMs` to `TestAgentRun`, computed in `TestAgentOverview.BuildAsync` with the injected `TimeProvider` (finished: end − start; running: now − start; otherwise null); replace the overview's private running set with `TestGenRunState.Running`; verify with xUnit tests in `TestAgentOverviewApiTests` for a finished, a running and an unknown run, and that `make lint-dotnet` is clean

## 3. Web: Duration column

- [x] 3.1 Add `elapsed(ms)` to `web/src/coverage/format.ts` (`42s`, `3m 05s`, `1h 02m`, `—` for null) and the three fields to `TestAgentRun` in `web/src/api/types.ts`; verify with Vitest cases for each range and null
- [x] 3.2 Add the Duration column before When in `TestAgentSection`, with "so far" and a one-second tick for running runs from `durationMs` plus the time since the answer; verify with Vitest cases (finished, running ticks with fake timers, unknown) and that the existing section tests pass; `make test-web` and `make lint-web` pass

## 4. Documentation

- [x] 4.1 Update the `test-agent` paragraph of `docs/http-api.md` (the `recent` fields and what `durationMs` means) and the `/admin/a2a` paragraph of `README.md`; run `make docs` and `make docs-check`; both succeed
