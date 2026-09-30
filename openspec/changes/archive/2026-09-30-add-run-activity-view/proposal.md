# Proposal

## Why

When a test-generation run starts, the Coverage screen replaces the threshold's Save with a single line, "Running ·
attempt 1/5 · 69% · target 90%". That line hardly moves for minutes. Nothing shows whether the agent is thinking,
writing a test, waiting for a build, or stuck. The data exists but never reaches the browser:
- the agent already reports a phase per attempt (`generating`, `building`, `testing`, `measuring`), and the api
  drops it;
- its tool calls (read, write, run tests, read coverage, report a suspected bug) are recorded nowhere;
- each attempt's outcome (coverage before and after, build result, test counts, errors) arrives only in the final
  report.

On the same screen, "The last refresh failed." stays up indefinitely, with no time and no reason. The job summary
("The job failed; see server logs.") is the same whether the runner was down or the stack was stopped mid-job,
which is what happened here.

## What Changes

- **Run activity, recorded.** The test agent reports what it is doing as it happens. It reports each phase, each
  tool call with a short structured summary (tool, path, outcome: for example `RunTests → build ok, 12 passed, 1
  failed, 72.1%`), each attempt's result, and the model's text as it streams, with its reasoning when the provider
  returns it. The api stores these entries per run, in order, without duplicates, and keeps them after the run
  ends.
- **One AG-UI stream to the browser.** The browser learns about a run only through AG-UI, the protocol the chat
  already speaks. `GET /api/coverage/runs/{id}/events` becomes an AG-UI event stream and replaces its custom
  `snapshot`/`update`/`end` frames:
  - `RUN_STARTED`, then `STATE_SNAPSHOT` with the run summary (now with its `phase`) first and on every change;
  - each activity entry as the protocol's own events: a phase as `STEP_STARTED`/`STEP_FINISHED`, a tool call as
    `TOOL_CALL_START`/`ARGS`/`END`/`RESULT`, model text as `TEXT_MESSAGE_START`/`CONTENT`/`END`, reasoning as
    `REASONING_*`;
  - an attempt's result as a `CUSTOM` event;
  - `RUN_FINISHED` when the agent's work ends, or `RUN_ERROR` when the run failed or was canceled.

  A late subscriber gets the whole run so far, then the live events. A finished run replays and ends at once, so no
  separate read endpoint is needed. The agent-to-api hop stays A2A.
- **Activity button and modal.** On the Coverage screen, an **Activity** button sits next to the file's Save. It
  also appears where Save is replaced while a run is active, and next to the run status a non-admin sees. It is shown
  whenever the file has a run. It opens a modal with the run's header (target, model, state, attempt and phase,
  coverage, tokens, cost, elapsed time) and a live timeline that follows new entries unless the user scrolls up.
  Model text and reasoning appear as collapsible blocks. An administrator can cancel an active run from the modal.
- **Content stays out of telemetry.** Model text, file paths and tool summaries go only to the run's activity
  record and the browser. They never go to logs, spans or metrics, as before. Each entry and each run has a size
  cap.
- **Refresh outcome explained.** A refresh that ends because the service stopped is reported as interrupted. A
  failure gives a user-facing reason: runner unavailable, no report produced, or `main` has no commit. The screen
  shows when the last refresh ended, why, and when the latest measurement was taken. The message goes away once a
  later refresh succeeds.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `test-generation-agent`: the agent reports its activity (phases, tool calls, attempt results, model text) to the
  api as the task runs.
- `test-generation-runs`: the api keeps an ordered, deduplicated activity record per run. The run's stream to the
  browser is AG-UI and carries the run's state (with its phase) and its activity. A finished run replays.
- `coverage-dashboard`: the Activity button and its live modal, including cancel for an administrator, and the
  explained refresh state.
- `coverage-ingestion`: a refresh's outcome distinguishes interrupted from failed and names the reason.

## Impact

- `src/Maf.Lab.TestGen` (a `testgen.activity/v1` contract), `src/Maf.Lab.TestAgent` (emit entries from the attempt
  loop, the tool layer and the chat stream).
- `src/Maf.Lab.Api`: a `TestGenRunActivity` table and migration, `RunFollower`/`TestGenRuns.ApplyAsync` persisting
  entries, the events endpoint rewritten as an AG-UI stream (a projection of the run and its activity into AG-UI
  events), `RunSummary.Phase`, `AdminJobRunner` (interrupted summary), `CoverageRefresher` (reasons).
- `web/src/coverage`: `RunActivityDialog`, the button in `ThresholdControl` and next to `LiveRunStatus`,
  `useRunEvents` reading the AG-UI stream (with `@ag-ui/core` types, as the chat does), and `RefreshControl`.
- No new package, model, make target, route or load-balancer location: one route changes its payload to AG-UI. Tenancy is untouched: coverage is
  repository-wide. No Jev call is added or changed.

## Documentation impact

- `docs/http-api.md`: the `/runs/{id}/events` row describes the AG-UI stream and its event mapping, and the
  refresh job's summary reasons.
- `docs/telemetry.md`: one sentence saying that run activity (model text, paths) is kept in the api's store for the
  Coverage screen and is never emitted as telemetry.
- `README.md`: not affected. It does not describe the Coverage screen, only the `make coverage` target, which
  does not change.
- `CLAUDE.md`, `openspec/project.md`, `.github/copilot-instructions.md`: not affected.
