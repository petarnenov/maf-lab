# Tasks

The working tree holds a first implementation that decided the stop on the screen (a local `stopped` action, the
stopped run's events dropped). The design replaces that; the tasks below rework it, and a task is ticked only once its
verification passes.

No Jev call is added or changed, so the Jev review checklist does not apply.

## 1. The test runtime

- [x] 1.1 In `web/src/test/agentFetch.ts`, answer a stop as CopilotKit's runtime does: a stop for a thread whose run is
      still streaming ends that stream with `RUN_ERROR { code: "abort" }` (after closing an open text message, as
      `wellFormed` does), and still answers the stop request itself. Verify with the page tests in 3.2 and that the
      existing web tests still pass.

## 2. The run

- [x] 2.1 In `chatReducer.ts`: replace the `stopped` action with `stop_requested` (sets `stopping` on the streaming
      turn); route `RUN_ERROR` with `code` `abort` and `RUN_FINISHED` with outcome `cancelled` to `stopped(state)`; a
      `TOOL_CALL_RESULT` whose content has `status: "stopped"` closes its card as stopped. Verify with reducer tests:
      - `stop_requested` marks the turn stopping and changes nothing else; with no streaming turn the state is unchanged;
      - events after `stop_requested` still reach the turn;
      - each of the two terminal events → `done`, `stopped`, text kept, reasoning time closed, running cards stopped
        (finished, `stopped`, not `isError`), no `step`, no `error`, no `turnId`, `streaming` false;
      - a stopped tool result → card finished, `stopped`, not `isError`;
      - a confirmation in `answering` → `waiting`;
      - any other `RUN_ERROR` is still a failure with its face.
- [x] 2.2 In `useChatStream.ts`: `cancel()` calls only `copilotkit.stopAgent({ agent })`, once per run, after
      dispatching `stop_requested`; the `onEvent` guard drops only a run that was replaced; `run()` resolves `'stopped'`
      when its terminal event was a stop, and `answer()` then dispatches nothing. Verify that `npx tsc -b --noEmit` in
      `web/` passes, and through the page tests in 3.2–3.3.

## 3. The chat screen

- [x] 3.1 In `ChatPage.tsx` and `ChatPage.module.css`: the Esc listener on `window` while a run is in progress
      (skipping `defaultPrevented` and `isComposing`), "Esc to stop" for as long as the turn streams, "Stopping…" in its
      place once asked, "Stopped." under a stopped turn, a stopped tool card shown as stopped, all in the page's tokens.
      Verify with 3.2 and by eye in both themes.
- [x] 3.2 Rework `ChatPage.stop.test.tsx`, one test per `web-ui` scenario of "Esc stops the answer in progress",
      through the stop request and the terminal event the test runtime sends:
      - stopping part-way (`RUN_ERROR abort`): the stop request names the thread and run, the text stays, "Stopped.",
        no progress, no `turn-error`, Send enabled;
      - a `RUN_FINISHED cancelled` terminal event: "Stopped.", no error;
      - before the terminal event: "Stopping…", no "Stopped.", a second Esc sends no second stop, streamed text still
        appears in the turn (the test holds the stop's terminal event back for this);
      - Esc with focus outside the composer asks for the stop;
      - a `TOOL_CALL_RESULT { status: "stopped" }` shows the card as stopped;
      - a card still running when `RUN_ERROR abort` arrives shows as stopped;
      - Esc with nothing running sends no stop and changes nothing;
      - Esc that cancels a history rename sends no stop and the run goes on;
      - a message sent after the stop goes to the same thread and streams into a new turn;
      - the hint shows while running and is gone after the run ends and after a stop.
      Verify that `npm test -- --run` in `web/` passes.
- [x] 3.3 In `ChatPage.confirmation.test.tsx`: approve, Esc, the answer run ends with `RUN_ERROR abort`; the proposal
      shows waiting again, and approving again sends the same idempotency key. Verify that it passes.

## 4. The agent's work

- [x] 4.1 In `ComplianceConsultant`, send `tasks/cancel` for a review in flight when the run's own token is cancelled
      (not on the deadline), under a short timeout of its own, recorded in the consultation audit as operation
      `cancel` with its outcome; a failed cancel is recorded and does not hold up the run. Verify with xUnit tests in
      `ComplianceConsultantTests` over the real reviewer: cancelling the caller's token mid-review sends `tasks/cancel`
      and the reviewer's task ends cancelled; a passed deadline sends none and keeps the task id; an unreachable
      reviewer during the cancel is recorded and the call still ends promptly.
- [x] 4.2 Add an integration test in `McpServerTests`: a `tools/call` whose client token is cancelled mid-call is
      cancelled on the server and does not complete. Record in design.md which SDK mechanism carried it. Verify that
      it passes against the test server.
- [x] 4.3 Verify the tool audit records a tool call interrupted by a stop with a cancelled outcome and its duration
      (chat-agent "A stopped run stops its work"); if it does not, record the cancellation in the audit wrapper before
      rethrowing. Verify with an xUnit test.

## 7. A cancel wherever the work runs

- [x] 7.1 In `RedisTaskStore.SaveTaskAsync`, save through one Lua script that keeps a stored terminal state against a
      different incoming one, with the terminal names taken from the SDK's serialisation of `TaskState`. Verify with an
      integration test against a real Redis (compose's `redis:8.8.3-alpine` through Testcontainers' generic container):
      `canceled` then `working` → stays `canceled`; `canceled` then `completed` → stays `canceled`; `working` then
      `completed` → `completed`; the index still lists the task. Keep `RedisTaskStoreTests` passing.
- [x] 7.2 Add `TaskCancelWatch` to `Maf.Lab.A2A` (polls the store every `CancelPollEvery`, default 1 s; cancels a token
      when the task is `canceled`; stops with the run). Verify with unit tests: it cancels on `canceled`, not on
      `working` or `completed`, and stops polling once disposed.
- [x] 7.3 Run `ReviewAgentHandler`'s stages under the watch's token. Verify with a test of two reviewer hosts over one
      store (with a test store that has the same terminal guard): review on one, `tasks/cancel` through the other; the
      task ends `canceled`, no further stage update is written, and it is still `canceled` after the review's duration.
- [x] 7.4 Link the watch into `TestGenerationHandler.RunOwnedAsync` and mark the task canceled when it fires, so the
      run's end removes the checkpoint and the lease. Verify with a test of two test agent hosts over one store:
      run on one, `tasks/cancel` through the other; no further model call or test run starts, the task stays
      `canceled`, and the checkpoint and lease are gone.

## 5. Checks

- [x] 5.1 Run `make lint` and `make test`, and verify both pass.
- [x] 5.2 With the stack rebuilt (`make`), on http://localhost:7171/chat: ask a question, press Esc part-way, and
      verify "Stopping…" then "Stopped.", the stop request and the `RUN_ERROR abort` on the run's stream, the
      `search_documents` call cancelled on `mcp-retrieval`, no turn recorded; send again and verify the conversation
      continues. Then ask for a fee adjustment above the review threshold, press Esc while the compliance check runs,
      and verify the `tasks/cancel` in the consultation audit, the compliance task `canceled` in Redis — and still
      `canceled` after the review's duration — whichever replica ran it.

## 6. Documentation

- [x] 6.1 No document is made untrue (proposal, Documentation impact). Run `make docs` and verify it changes nothing,
      then run `make docs-check` and verify it passes.
- [x] 6.2 Run `npx --yes @fission-ai/openspec@1.13.1 validate esc-stops-chat-run --strict` and verify it passes.
