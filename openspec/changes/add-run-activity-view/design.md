# Design

## Context

- The agent runs each attempt with `agent.RunAsync(...)` (non-streaming) through a `FunctionInvokingChatClient`
  whose invocation hook already sees every tool call. It reports progress through the A2A `TaskUpdater` as
  `testgen.progress/v1` data parts (attempt, phase, pct, tokens, cost).
- The api's `RunFollower` subscribes to the task (resubscribe, then polling fallback), and `TestGenRuns.ApplyAsync`
  writes each observation to `TestGenRuns` (SQLite on the shared `api-data` volume, used by both replicas).
  `TaskObservation.Progress` carries the phase, but `RunSummary` has no field for it.
- `GET /api/coverage/runs/{id}/events` polls the database every `EventPollEvery`. Any replica can serve the stream,
  whichever replica follows the task.
- The chat already speaks AG-UI to the browser: `AGUI.Server`/`AGUI.Abstractions` on the api (`AGUIStream` for
  serialization and frame names), and `@ag-ui/core` types with a small SSE reader on the web. The user decided that
  the browser learns about a run only through AG-UI, while the agent-to-api hop stays A2A.
- The telemetry specs forbid content in logs, spans and metrics. Chat history already stores message content in the
  api's database, so storing run activity there fits the same rule.

## Goals / Non-Goals

**Goals:** a live, replica-safe activity timeline per run, kept after the run; no content in telemetry; no effect
on the run if activity cannot be delivered.

**Non-Goals:**
- Changing the agent-to-api hop: it stays A2A (`test-generation-agent`: A2A-only surface).
- Token-by-token rendering. Chunks every ~2 s are enough and keep the A2A and database traffic small.
- Showing the diff live. It stays in the final report and the candidate panel.
- Replaying activity for runs from before this change. Those show "No activity was recorded for this run."
- A new admin-job state. `failed` stays and the summary carries the reason, so Index admin's jobs keep their
  contract.

## Decisions

1. **One contract, `testgen.activity/v1`**, in `Maf.Lab.TestGen`: `TestGenActivity(kind, seq, at, attempt, type,
   phase?, tool?, result?, continues?, text?, truncated)`. `type` is `phase|tool|attempt|text|reasoning`. A text
   chunk names the `seq` of the entry it continues. `tool` is `ToolActivity(Name, Path?, Outcome, Summary)` and
   `result` is `AttemptActivity(Before, After, Build, Tests, Errors, Violations)`.
   **Transport:** each batch of entries is its own A2A artifact, named `testgen-activity` with a fresh artifact id.
   It is not one artifact grown with `append`, because the shared-store resubscription in
   `A2ARequestHandlerWithExtras` announces only artifact ids it has not seen, so appended parts would never reach a
   resubscribed follower. As separate artifacts, the task snapshot a resubscription or a poll returns holds every
   entry so far, and new batches arrive as artifact updates. The api deduplicates by `seq`.
2. **Where the agent emits:**
   - phases in `ProgressAsync` (already called at each phase);
   - tools in the existing function-invocation hook, after the call: name, the `path` argument when the tool has one,
     and a summary built from the tool's DTO result (`Written`, the run-tests result, `Coverage`, `BugRecorded`),
     never its file text;
   - attempts where `AttemptLog` is built;
   - text and reasoning by switching the attempt from `RunAsync` to `RunStreamingAsync` (Microsoft Agent Framework,
     same agent, same tools) and buffering `TextContent` / `TextReasoningContent` updates into chunks flushed at 2 s
     or 1 KB.
   An `ActivityReporter` holds the `seq` counter and the current attempt, and swallows send failures. It counts
   them and logs them without content.
   *Alternative:* a side channel (the agent posts to the api). This adds a second path between the services and
   breaks "the api is the only A2A client". It was rejected.
3. **Storage:** a table `TestGenRunActivity(RunId, Seq, LastSeq, At, Attempt, Type, Phase, DataJson, Text,
   Truncated)` with key `(RunId, Seq)`, created by `DatabaseInitializer` (the project has no EF migrations: tables
   and columns are added idempotently on start). A new entry is inserted unless its `(RunId, Seq)` exists. A chunk
   is applied to its root row only if its `seq` is greater than the row's `LastSeq`, which advances. Text is capped
   at 4 KB and then marked `Truncated`. The caps are 2 000 rows and 1 MB per run; when they are exceeded, the
   oldest rows are deleted and `ActivityDropped` is set on the run.
4. **Serving AG-UI:** `RunActivityProjection` turns the run summary and its rows into AG-UI events. It is
   deterministic, so any replica produces the same stream from the database.
   - `RUN_STARTED` (threadId `testgen:<id>`, runId `<id>`), then `STATE_SNAPSHOT` with the summary.
   - A phase row: `STEP_FINISHED` for the open step, then `STEP_STARTED` `attempt N: <phase>`.
   - A tool row: `TOOL_CALL_START`/`ARGS`/`END`/`RESULT` with call id `tool-<seq>`, args `{"path":…}`, and result
     `{"outcome":…,"summary":…}`.
   - A text row: `TEXT_MESSAGE_START` (messageId `text-<seq>`, role assistant) and `TEXT_MESSAGE_CONTENT` for its
     text. When the row grows later, only the new suffix is sent as `CONTENT`. `TEXT_MESSAGE_END` follows when a
     later row of another kind arrives or the run's work is over.
   - Reasoning in the same shape, with `REASONING_START`/`REASONING_MESSAGE_START`/`CONTENT`/`END`/`REASONING_END`.
   - An attempt row: `CUSTOM maf-lab/testgen-attempt`.
   - Dropped rows: `CUSTOM maf-lab/testgen-activity-dropped`.

   The stream polls every `EventPollEvery` for rows with `LastSeq` past its cursor and for changes of the summary,
   and emits the projection's new events. It ends with `RUN_FINISHED` (result: the summary) at `candidate` or a final
   state other than failed/canceled, and with `RUN_ERROR` (code: the reason) for those two. A run already past that
   point replays and ends. Frames are named by the event type (`AGUIStream.FrameName`) and serialized with
   `AGUIStream.Json`. `RunSummary` gains `Phase`.
5. **Web:** a `RunActivityDialog` built from the existing dialog styles (`RaiseThresholdDialog`), with focus trap,
   Escape to close and focus returned to the button. `useRunStream(runId)` reads the run's AG-UI stream with the
   chat's SSE parser and the `@ag-ui/core` event types. It reduces the stream into `{ summary, timeline, ended }`:
   steps, tool calls joined by call id, text and reasoning messages joined by message id, and attempt customs. It
   serves both the modal and the one-line status, so `useRunEvents` becomes a thin wrapper around it. Auto-follow is kept while the user is at
   the bottom; otherwise a "New activity ↓" pill appears. The button sits next to Save in `ThresholdControl` and in
   its locked branch, and next to `LiveRunStatus` for a non-admin. Cancel calls the existing
   `POST /runs/{id}/cancel`.
6. **Refresh reasons:** `AdminJobRunner` sets the summary "Interrupted: the service stopped while the job ran." when
   `ApplicationStopping` caused the exception. On startup, the existing stale-heartbeat expiry gives stale jobs the
   same summary. `CoverageRefresher` throws a `CoverageRefreshException(reason)` whose user-facing message becomes
   the summary (runner unavailable, no report, no commit). `RefreshControl` shows the summary with `finishedAt`, plus
   the tree's newest `measuredAt`, and only when the latest job is not `succeeded`.

7. **Found on the real stack.**
   - The baseline measurement is a whole build (~1.5 min for dotnet), and it used to happen before any progress,
     which left the run looking silent. The agent now reports phase `measuring` under attempt 0 before the
     baseline, and the projection names that step `baseline: measuring`.
   - The threshold control swaps its layout when a run's lock lifts, which remounted the modal just as the run
     ended. `useRunActivity` keeps the button and the dialog apart, and the dialog has one slot outside both
     layouts.
   - The api writes stored `DateTime`s without a zone. The Coverage screen now reads a zone-less time as UTC
     (`format.instant`), which also fixes "Measured" times shown in UTC as if they were local.

## Risks / Trade-offs

- [The switch to streaming changes how the agent calls the model] → Same agent, tools and budget client.
  `BudgetedChatClient` already counts streaming. The model-free e2e (stub) and `TestGenerationHandler` tests cover
  it, and the stub must stream (it already streams chat).
- [Model text in the database could hold repository source quoted by the model] → The repository is visible to any
  signed-in user on this screen anyway. The per-entry and per-run caps bound the volume, and nothing reaches
  telemetry. A test asserts the logs of a run hold none of the streamed text.
- [SQLite write volume during a run] → Chunks every 2 s plus tool and phase entries amount to a few rows a second at
  most, well under what the existing run updates already write.
- [An old run shows an empty modal] → It shows an explicit "No activity was recorded for this run.".

## Migration Plan

Additive schema: a new table, and `Phase` and `ActivityDropped` columns on `TestGenRuns`, added by
`DatabaseInitializer` on api start. The events route keeps its path but now speaks AG-UI. Its only consumer is this
web app, which changes in the same commit. Rolling back leaves an unused table.
