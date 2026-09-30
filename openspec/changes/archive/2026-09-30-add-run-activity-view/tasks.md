# Tasks

## 1. Contract

- [x] 1.1 Add `testgen.activity/v1` to `Maf.Lab.TestGen/AgentContracts.cs`: the entry record, its `type` values,
      and typed `data` records (phase, tool, attempt, text/reasoning with `truncated`). Verify with round-trip
      serialization tests.

## 2. Agent

- [x] 2.1 Add `ActivityReporter` (seq counter, sends data parts on `working` updates, swallows and counts failures
      without content). Verify with a test where the updater throws: the task still completes.
- [x] 2.2 Emit `phase` entries from `ProgressAsync`, and `attempt` entries where `AttemptLog` is built. Verify in
      `TestGenerationHandler` tests: entries in order, with the attempt's before and after coverage.
- [x] 2.3 Emit `tool` entries from the function-invocation hook, with summaries built from each tool's DTO (a
      refused write included) and no file text. Verify with tests for each tool, including the refused write
      scenario.
- [x] 2.4 Switch the attempt to `RunStreamingAsync`, buffering text and reasoning into chunks (2 s or 1 KB) with
      the 4 KB cap. Verify that a scripted streaming chat yields several chunks of one entry, and that
      `BudgetedChatClient` still counts the tokens.
- [x] 2.5 Make the CI Ollama stub's scripted test-agent reply stream in several chunks. Verify with `make ci-e2e`
      (task 6.2).

## 3. Api

- [x] 3.1 Add the `TestGenRunActivity` table and the `Phase` and `ActivityDropped` columns, created by
      `DatabaseInitializer`. Verify that the api starts on an existing database and the table and columns appear.
- [x] 3.2 Read `testgen-activity` artifacts into `TaskObservation`, and persist entries in `TestGenRuns.ApplyAsync`
      (insert-ignore on `(RunId, Seq)`, chunks applied by `LastSeq`, the caps with drop-oldest), and persist `Phase`. Verify with tests: a replay stores nothing twice, the caps hold,
      and appends concatenate.
- [x] 3.3 Add `RunActivityProjection` (summary and rows to AG-UI events), and rewrite `/runs/{id}/events` as an AG-UI
      stream: `RUN_STARTED`, `STATE_SNAPSHOT`, the backlog, live events, and one terminal `RUN_FINISHED`/`RUN_ERROR`.
      Verify with tests for each scenario of "Progress to the browser over SSE" (late subscriber, phase, tool call,
      failed run, replay), and for a second api host that reads the same database.
- [x] 3.4 Add a test that runs a scripted run and asserts that the captured logs and spans hold none of the
      streamed text and no tool path summary.

## 4. Refresh reasons

- [x] 4.1 `AdminJobRunner`: give the interrupted summary on shutdown and to jobs expired as stale. Verify with a test
      that stops the host mid-job.
- [x] 4.2 `CoverageRefresher`: `CoverageRefreshException` with the reasons (runner unavailable, no report, no
      commit). Verify each with `FakeCoverageRunner`.

## 5. Web

- [x] 5.1 Add `phase` to `RunSummary` in `api/types.ts`, show it in `RunStatus`, and add the activity types. Verify
      with a `RunStatus` test.
- [x] 5.2 `useRunStream(runId)`: read the AG-UI stream (the chat's SSE parser, `@ag-ui/core` types) into
      `{ summary, timeline, ended }`, and make `useRunEvents` use it for the one-line status. Verify with hook tests:
      backlog then live, a text message growing by content events, a tool call joined by id, and RUN_ERROR.
- [x] 5.3 `RunActivityDialog`: header, timeline (phase, tool, attempt, collapsible text and reasoning), auto-follow
      with the "New activity ↓" pill, cancel for an admin, focus trap, Escape, focus returned, and the empty state
      for runs without activity. Verify with Testing Library tests for each scenario of the requirement.
- [x] 5.4 Put the Activity button next to Save in `ThresholdControl`, in its locked branch, and next to
      `LiveRunStatus`, and only when the file has a run. Verify with `CoveragePage` tests (admin, non-admin, no
      run).
- [x] 5.5 `RefreshControl`: show the summary with the finish time and the latest measurement, only when the latest
      job did not succeed. Verify with tests for interrupted, runner down, and a later success.

## 6. Verify on the stack

- [x] 6.1 Run `make test-dotnet lint-dotnet test-web lint-web build-web`: all pass.
- [x] 6.2 Run `make ci-e2e`: it passes. The run's AG-UI stream (`/runs/{id}/events`, replayed after the run) holds
      step, tool call, text message and attempt events.
- [x] 6.3 On the dev stack (`make`), raise a file's threshold, open Activity, and watch the timeline fill live
      (phases, tool calls, model text). Close and reopen it mid-run; cancel from the modal. Take a screenshot for
      the verification note.
      Verified 2026-09-30 on the dev stack: a real run of `RedisIdempotencyStore.cs` to 85% with GLM 5.3. The modal
      filled live with reasoning, tool calls (paths, line counts, a refused `list_files`), the step and the header
      (Working · 1/5 · building · 101,194 tokens · $0.063); see `verification-activity.jpg`. It was closed with
      Escape, reopened mid-run, and the run was cancelled from it; the file view unlocked and the finished run
      replayed whole. Found and fixed while doing it: the modal remounted when the lock lifted (now one dialog slot
      outside both layouts); zone-less api times were read as local time (Elapsed showed 180 min; now read as
      UTC); and the baseline build left the run silent for ~1.5 min (now a `baseline: measuring` step).
- [x] 6.4 On the dev stack, stop the stack mid-refresh (`make down` while `make coverage` runs), then run `make`. The
      screen shows the interrupted message with its time, and a successful `make coverage` clears it.
      Verified 2026-09-30: a refresh started with the api, then `make down` and `make`. The job ended failed with
      "The job was interrupted (its server instance stopped); start it again.", and the screen showed it with its
      time and the current measurement's time. A later `make coverage` succeeded and the message was gone.

## 7. Documentation

- [x] 7.1 `docs/http-api.md`: the events row describes the AG-UI stream and its mapping; add the refresh summary
      reasons.
- [x] 7.2 `docs/telemetry.md`: run activity is stored for the Coverage screen and never emitted as telemetry.
- [x] 7.3 Run `make docs`, then `make docs-check` and `make specs`. All pass.
