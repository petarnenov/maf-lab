# Design

## Context

See proposal.md for why. What exists today:

- The table is the "Recent runs" table of `web/src/admin/TestAgentSection.tsx` on `/admin/a2a` (columns File, State,
  Attempt, Coverage, Reason, Model, When). It is the only screen that lists runs this way; the Coverage page shows one
  run per file in its own panel and is not part of this change.
- It is fed by `GET /api/admin/a2a/test-agent` (`Coverage.TestAgentOverview.BuildAsync`), whose `recent` items are
  `TestAgentRun` DTOs built from the ten `TestGenRuns` rows with the latest `UpdatedAt`.
- A `TestGenRunRow` has `CreatedAt` (the start) and `UpdatedAt` (the last change of any kind), both UTC `DateTime`.
  There is no end-of-work time. `UpdatedAt` is not one: an accepted or discarded run's last change is the person's
  decision, which can come hours after the agent and verification finished.
- A run's state moves in exactly two places, `TestGenRuns.ApplyAsync` (observations of the agent's task) and
  `TestGenRuns.FinishAsync` (every other move: verification results, candidate, accept, discard, cancel, deadline,
  start failures). Every move also appends a `TestGenRunEvents` row holding the run summary as JSON, with its time.
- The schema is created by `DatabaseInitializer`, which adds a nullable column the model maps but a table lacks, and
  runs idempotent backfills at startup. No EF migrations.
- The overview is fetched once per page open or Refresh (`refetchOnWindowFocus` is off app-wide); no polling.

## Goals / Non-Goals

**Goals:**
- A duration that means the run's work: agent plus verification, not the wait for a person.
- Computed by the api, so the browser's clock and time zone do not matter.
- Existing rows get a sensible value without a manual step.

**Non-Goals:**
- No duration on the Coverage page, the run SSE stream or `RunSummary` (it could reuse the column later).
- No polling of the overview: a running run's value counts locally and is corrected on the next refresh.
- No per-attempt or per-phase timing.

## Decisions

1. **Duration = work time: from `CreatedAt` to the first time the run leaves `submitted`/`working`/`verifying`.**
   Entering `candidate` or any final state ends the work. Alternatives: `UpdatedAt − CreatedAt` — rejected, an
   accepted run would include the reviewer's delay and keep growing with any later touch; time to the final state —
   rejected for the same reason, and a candidate would have no duration until someone decides.

2. **Store it: a nullable `FinishedAt` (UTC `DateTime`) on `TestGenRunRow`**, stamped once by one helper
   (`TestGenRuns.StampFinish`) called from both `ApplyAsync` and `FinishAsync` after the state is set: when the new
   state is not a running state and `FinishedAt` is null, it becomes the same `now` written to `UpdatedAt`. A later
   accept or discard sees it set and leaves it. The running states are named once, as `TestGenRunState.Running`
   (the overview's private copy of that set is replaced by it). Alternative: derive the end from `TestGenRunEvents` on
   every read — rejected, it parses JSON for ten runs on each page load and couples the overview to the event format.

3. **Backfill once from the events.** `DatabaseInitializer.BackfillAsync` sets `FinishedAt` for rows that are not
   running and have none: the `MIN("At")` of the run's events whose `json_extract("Json", '$.state')` is not a running
   state (the earliest stop, exact for every run that has events), else `UpdatedAt`. It only touches rows with a null
   `FinishedAt`, so it is idempotent and a second replica running it at the same time writes the same values.
   `json_extract` is in the SQLite that Microsoft.Data.Sqlite bundles. Alternative: leave older rows without a
   duration — rejected, the screen would show `—` for every run that exists today.

4. **DTO: `startedAt`, `finishedAt`, `durationMs`.** Added to `TestAgentRun` after `updatedAt` (additive JSON).
   `durationMs` is `finishedAt − startedAt` when `finishedAt` is set; `now − startedAt` from the injected
   `TimeProvider` when the run is running; null otherwise (a stopped run with no end — only possible if the backfill
   could not run). Negative values (clock moved) are clamped to 0. Computing it on the api keeps the one rule in one
   place and avoids browser clock skew; `startedAt`/`finishedAt` are there so the meaning can be read, not recomputed.

5. **Web: a `Duration` column before `When`, formatted by `elapsed(ms)` in `web/src/coverage/format.ts`.** `42s` under
   a minute, `3m 05s` under an hour, `1h 02m` beyond; `—` for null. For a running run the cell reads "`42s` so far"
   and ticks: the component adds `Date.now() − dataUpdatedAt` (React Query's time of the answer) to `durationMs`, with
   a one-second interval that runs only while a listed run is running. Using the answer's arrival time rather than
   `startedAt` keeps the browser clock out of it. Alternative: refresh the overview every few seconds — rejected, each
   refresh may probe the agent's card and re-reads counts; one local timer is enough for a number that only grows.

## Risks / Trade-offs

- [A run stuck in `verifying` after a crash keeps counting] → That is what is happening (it is not finished); the
  existing follower/deadline logic is what ends it, and its end is then stamped.
- [Backfill for a run with no events uses `UpdatedAt`, which for an accepted run includes the review wait] → Only
  rows written before events existed; acceptable, and stated in the docs.
- [The ticking value drifts from the server by the request latency] → Under a second; the next refresh replaces it.

## Migration Plan

Additive: the column is created by the existing additive pass at api start and filled by the backfill. Rolling back
leaves an unused nullable column, which older code ignores.
