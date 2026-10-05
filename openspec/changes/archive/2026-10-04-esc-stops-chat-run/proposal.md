# Proposal

## Why

Once a question is sent, the chat screen gives the person no way to stop the answer: a wrong question, or an answer
heading somewhere useless, runs to its end while the composer stays locked on "Answering…". Esc to stop is the basic
gesture every chat assistant offers. Stopping must reach every party doing work for the run — the agent, its MCP
tools, and an A2A agent it is consulting — and each of them must be told the way its own protocol says, with nothing
of our own invented on the way.

## What Changes

- On the chat screen, Esc stops the run in progress — an ordinary answer or the answer that follows an approval or a
  rejection — wherever the focus is on the page, and only while a run is in progress.
- **The stop travels only by the protocols' own means:**
  - browser → runtime: CopilotKit's `stopAgent` (its stop request for the thread and run);
  - runtime → agent: CopilotKit's runner aborts the AG-UI run (`abortRun`), which ends the run's request to the
    api's `MapAGUIServer`;
  - agent → MCP tools: the run's cancellation reaches each tool call in flight through the MCP SDK's own cancellation;
  - agent → A2A agent: a compliance review in flight is cancelled with A2A `tasks/cancel`, and the cancel stops the
    review whichever replica runs it — the shared task store is where it is known, with no sticky routing. The test
    agent's runs get the same guarantee.
  No endpoint, event, message or request of this system's own is added anywhere along the chain.
- **The screen renders the stop from the run's own AG-UI events, as CopilotKit delivers them.** Esc changes nothing in
  the conversation by itself: the turn shows "Stopping…" until the stopped run's terminal event arrives —
  `RUN_ERROR` with `code: "abort"` (what `@ag-ui/client` reports for an aborted run) or `RUN_FINISHED` with
  `outcome: { type: "cancelled" }` (what CopilotKit's runtime appends for a stopped run). Then the turn keeps what it
  had streamed and says "Stopped.", as a stop, not as an error. A `TOOL_CALL_RESULT` whose content says
  `status: "stopped"` closes its tool card as stopped, not failed. No local "stopped" state, no timeout.
- Stopping the answer to an approval or a rejection puts the proposal back to waiting; answering it again carries the
  same idempotency key, so nothing is applied twice.
- An Esc that a control on the page has already handled for itself (cancelling a rename in the history sidebar) does
  not stop the run; nor does an Esc pressed while an input method is composing.
- While a run is in progress, the turn shows "Esc to stop" in the page's theme.
- Other screens (coverage runs, admin jobs) are out of scope.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-ui`: a new requirement, "Esc stops the answer in progress", on the chat screen.
- `chat-stream`: a scenario on "Answer runs remain stream-backed" for an answer run that is stopped — the proposal is
  waiting again.
- `chat-agent`: a new requirement, "A stopped run stops its work": the agent's tool calls in flight are cancelled on
  their MCP servers by the MCP SDK's own cancellation, and the stopped run records no turn.
- `a2a-client`: a new requirement, "A stopped consultation is cancelled over there": a review in flight when the run
  is stopped is cancelled with `tasks/cancel`.
- `compliance-review`: a new requirement, "A cancel stops the review wherever it runs": a terminal task is never
  overwritten in the shared store, and the replica running a review notices a cancel there and stops.
- `test-generation-agent`: a new requirement, "A cancel stops the run wherever it runs", the same for the test agent.

## Impact

- Web: `web/src/chat/chatReducer.ts` (the stopped run's terminal events and stopped tool results; a "stopping" mark
  on the turn), `web/src/chat/useChatStream.ts` (`cancel` calls only `stopAgent`; the stopped run's events still reach
  its turn; `run` tells a stop apart by its terminal event), `web/src/chat/ChatPage.tsx` and `ChatPage.module.css`
  (the Esc listener, the hint, "Stopping…" and "Stopped."), and their Vitest tests. The test runtime
  (`web/src/test/agentFetch.ts`) answers a stop as CopilotKit's runtime does.
- api: `src/Maf.Lab.Api/A2A/ComplianceConsultant.cs` sends `tasks/cancel` for a review in flight when the run's own
  cancellation fires (not on its deadline), recorded in the consultation audit like any other outcome; xUnit tests.
- MCP: no code change; a test proves a cancelled `tools/call` is cancelled on the server.
- Shared A2A code (`src/Maf.Lab.A2A`): `RedisTaskStore.SaveTaskAsync` refuses, atomically (one Lua script), to write a
  different state over a terminal one; a small watcher reads the task from the store while it runs and cancels the
  run's token when the store says `canceled`. Used by `ReviewAgentHandler` (compliance) and `TestGenerationHandler`
  (test agent). Found live: a cancel received by one compliance replica did not stop the review running on the other,
  which then saved the task `completed` over it.
- Tests: an integration test of the store's guard against a real Redis (the compose image, through Testcontainers'
  generic container — no new package), and two-replica tests over one store for both agents.
- No package moves, no new endpoint, no AG-UI event built outside the official libraries. Progress for the run is the
  existing themed `Progress`, with the hint and "Stopping…" under it (progress-feedback).

## Documentation impact

- `docs/http-api.md` (compliance consultation): a stopped run cancels the review in flight with `tasks/cancel`,
  recorded as `a2a.consult.cancel`, and the cancel reaches whichever reviewer replica runs it.
- No other document describes the chat screen's keys or the stop chain, and no route, target, project, model or lb
  location changes.
