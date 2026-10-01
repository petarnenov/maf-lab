# Proposal

## Why

The "Recent runs" table of the test-generation agent section on the Agent to agent page (`/admin/a2a`) says what
each run did and when it last changed, but not how long it took. An operator comparing models, limits or files has to
open each run and work it out from timestamps — and for a run that was accepted later, the last change is the
person's decision, not the end of the work. The store does not even record when a run's work ended.

## What Changes

- A run records when its work ended (`FinishedAt`): the first time it leaves the running states (`submitted`,
  `working`, `verifying`) — on reaching `candidate` or any final state. A later decision on a candidate (accept,
  discard) does not move it. Runs stored before this change get it once, from their event history (the time of their
  first event in a non-running state), falling back to their last change.
- Each recent run in `GET /api/admin/a2a/test-agent` gains `startedAt`, `finishedAt` (null while running) and
  `durationMs`: the work time `finishedAt − startedAt` for a run that stopped, the time so far (as of the answer) for a
  running one, and null when it is not known. Computed by the api, so the browser's clock does not matter.
- The Recent runs table gains a **Duration** column, before **When**, in the compact form `42s`, `3m 05s`, `1h 02m`,
  `—` when unknown. A running run reads "… so far" and ticks once a second on the page, from the api's value plus the
  time since that answer arrived; the next refresh replaces it with the api's value.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `test-generation-runs`: a run records when its work ended (new requirement).
- `a2a-observability`: the test-generation agent overview lists each recent run's start, end and duration.
- `web-ui`: the A2A screen's recent runs show a duration.

## Impact

- Code: `src/Maf.Lab.Api/Storage/CoverageRows.cs` (one nullable column, picked up by the existing additive schema
  pass; a backfill in `DatabaseInitializer`), `src/Maf.Lab.Api/Coverage/TestGenRuns.cs` (stamp the end in the two
  places a run's state moves), `src/Maf.Lab.Api/Coverage/TestAgentOverview.cs` and its endpoint (the DTO fields, with
  `TimeProvider` for "now"), `web/src/admin/TestAgentSection.tsx`, `web/src/coverage/format.ts`,
  `web/src/api/types.ts`. Tests in `tests/Maf.Lab.Tests` and `web/src`.
- API: three additive fields on each `recent` item; no new route, no parameter. Runs stay repository-wide; no tenant
  parameter is added anywhere.
- No package, model, Jev call, make target, CLI tool or load-balancer location changes. No new UI-started process:
  the duration is read with the overview, which already shows the themed progress indicator while it loads.

## Documentation impact

- `docs/http-api.md`: the `test-agent` paragraph lists the new `recent` fields and says what `durationMs` means for a
  running, a stopped and an older run.
- `README.md`: the `/admin/a2a` paragraph says the recent runs show how long each took.
- CLAUDE.md, openspec/project.md, docs/shared-state.md and .github/copilot-instructions.md do not describe this table
  and are unaffected.
