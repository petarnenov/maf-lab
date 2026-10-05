# Design

## Context

What the libraries in use do on a stop, read from their code (CopilotKit 1.76.0, `@ag-ui/client` 1.0.1) and observed on
the running stack:

- **Browser.** `copilotkit.stopAgent({ agent })` on a run's `ProxiedCopilotRuntimeAgent` POSTs
  `/copilotkit/agent/chat/stop/<threadId>` with `{ runId }`. It does **not** end the browser's own run stream: that
  stays open and carries the run's remaining events.
- **Runtime.** `InMemoryAgentRunner.stop` marks the run stop-requested and calls `abortRun()` on its `HttpAgent`, which
  aborts the run's request to the api. `@ag-ui/client` turns that `AbortError` into `RUN_ERROR { code: "abort" }` and
  completes. When a stop-requested run ends with no terminal event, the runner's finalizer
  (`@copilotkit/shared` `finalizeRunEvents`) closes what is open — `TOOL_CALL_END`, `TOOL_CALL_RESULT` with content
  `{ status: "stopped", reason: "stop_requested" }`, and `RUN_FINISHED { outcome: { type: "cancelled" } }` (protocol
  version 1.0, which the browser sends). Observed: the abort `RUN_ERROR` arrives first, so on this stack the terminal
  event is `RUN_ERROR { code: "abort" }`; the finalizer's events are the other official form and must be read too.
- **api.** The aborted request cancels `RequestAborted`; `ChatTurnRunner` rethrows the cancellation (no turn is
  recorded); `RunTap` marks the run's shared state cancelled. Observed: a `search_documents` call in flight was
  cancelled on `mcp-retrieval` at the moment of each stop.
- **A2A.** `ComplianceConsultant` streams a review with `SendStreamingMessageAsync` under the run's token linked to its
  deadline. On the run's cancellation the stream just ends; nothing tells the compliance agent, whose
  `ReviewAgentHandler` does support `tasks/cancel`. `TestAgentClient.CancelAsync` already cancels its own tasks that way.

The first draft in the working tree dispatched a local `stopped` action and dropped the stopped run's events. That
decides on the screen what only the protocol may say, and is replaced below.

- **Replicas.** Both A2A agents run as two replicas behind the balancer and keep their tasks in the shared
  `RedisTaskStore` (`task:<keyspace>:<id>`), so a task started on one is readable on the other. What is *not* shared is
  the running work: a review or a test run is a loop in one process with its own `CancellationToken`. Observed live: the
  review streamed through replica `.12`, `tasks/cancel` reached `.14`, which answered 200, and `.12` went on and saved
  the task `TASK_STATE_COMPLETED` 22 s later. Two causes: the running loop never reads the store, and
  `SaveTaskAsync` is a blind `SET`, so the last writer wins over a terminal state. The test agent has the same shape
  (`_running` is per process).

## Goals / Non-Goals

**Goals:**
- Every hop of the stop uses its protocol's own mechanism; nothing of our own on the wire.
- The screen shows the stop only from the run's own AG-UI events.

**Non-Goals:**
- A Stop button (can follow as its own change).
- Recording a stopped turn in history.
- Esc on other screens.

## Decisions

**Esc → `cancel()` → `copilotkit.stopAgent({ agent })`, and a `stop_requested` mark on the turn.** `cancel()` no longer
clears `agentRef` and no longer dispatches a result. It dispatches `stop_requested`, which sets `stopping: true` on the
streaming turn (the screen says "Stopping…") and nothing else, and calls `stopAgent` once — a turn already `stopping`
asks for no second stop. The mark is the screen's own record that a request is out, like "Answering…" on the button;
it never ends the turn. Alternative — keep the local `stopped` result: rejected, it would show a stop the protocol has
not reported.

**The terminal events decide, in the reducer.** `RUN_ERROR` with `code === 'abort'` and `RUN_FINISHED` with
`outcome.type === 'cancelled'` go to one `stopped(state)`: `status: 'done'`, `stopped: true`, `stopping` cleared,
reasoning time closed, `step` cleared, running tool cards closed as stopped (finished, `stopped`, no `isError`),
`streaming: false`,
and a confirmation in `answering` back to `waiting`. It records no `turnId`, because the server recorded no turn.
`TOOL_CALL_RESULT` whose JSON content has `status: "stopped"` closes its card with `stopped: true` and no error. These
are read whatever caused the abort; a run aborted for another reason (the browser went away) is also a stopped run.

**The stopped run's events keep reaching its turn.** The `onEvent` guard stays only for a run that was *replaced*
(`reset`, `hydrate`, a new run), which is what `stop()` with `agentRef` cleared means; `cancel()` leaves `agentRef` as
it is, so the run's own events, terminal one included, reach its turn. While the turn is streaming the composer stays
locked, so no other turn can be streaming at the same time.

**`run()` resolves `'stopped'` when its terminal event was a stop.** The subscriber notes it as it sees the event;
`answer()` then dispatches no `answered`, and the reducer has already put the proposal back to waiting. The idempotency
key is derived (`keyFor(adjustmentId, approve)`), so answering again replays rather than applies twice.

**A2A: `tasks/cancel` on the run's cancellation, not on the deadline.** In `ComplianceConsultant`, an
`OperationCanceledException` while `ct.IsCancellationRequested` (the run's own token, not the deadline) with a known
task id sends `CancelTaskAsync(new CancelTaskRequest { Id = taskId })` under a short timeout of its own
(`CancellationToken.None` linked to a few seconds — the run's token is already cancelled), records it in the
consultation audit as operation `cancel` with its outcome, swallows a failure after recording it, and rethrows the
cancellation. Same shape as `TestAgentClient.CancelAsync`. Alternative — rely on the closed stream: rejected, closing
an A2A stream does not cancel its task.

**MCP: prove it, change nothing unless the proof fails.** An integration test cancels a `tools/call` from the MCP
client and asserts that the server's handler sees its token cancelled and the tool does not complete. Shown by the test
(SDK 2.2.0, stateless Streamable HTTP, spec revision 2026-07-28): the client sends no `notifications/cancelled` — only
`server/discover` and `tools/call` go on the wire — and the cancellation reaches the server because the `tools/call`
request itself ends, which the stateless transport turns into the handler's cancellation. That is the transport's own
mechanism, so nothing is added. A tool call interrupted this way is audited with a cancelled outcome; if the
audit misses it today, the audit wrapper records the cancellation before rethrowing.

**The test runtime plays CopilotKit's stop.** `agentFetch` today answers a stop with `{ stopped: true }` and leaves the
stream alone. It changes to do what the runtime does: on a stop for a run it holds, it ends that run's stream with
`RUN_ERROR { code: "abort" }`; a test can also script `TOOL_CALL_RESULT { status: "stopped" }` and
`RUN_FINISHED { outcome: { type: "cancelled" } }` itself. Tests then exercise the real path: Esc → stop request →
terminal event → turn.

**Hint and notes in the page's tokens.** "Esc to stop" as its own line for as long as the turn streams (the progress
block goes away once text starts), "Stopping…" in its place once asked, "Stopped." as a quiet `role="status"` line.
`--muted`, `--border`, `--surface-2`, so they follow the theme.

**The shared store is where a cancel lives; no sticky routing.** Two parts, both inside the agents, nothing on the
wire:
- *The store never undoes an end.* `RedisTaskStore.SaveTaskAsync` runs one Lua script: read the stored task's
  `status.state`; if it is terminal and the incoming state differs, keep it and report that the write was refused;
  otherwise `SET` with the retention and update the index. The terminal state names are passed in as arguments, taken
  from the SDK's own serialisation of `TaskState`, so the script does not hard-code the wire spelling. Atomic in Redis,
  so a replica that read `working` a moment before another wrote `canceled` still cannot overwrite it. Alternative —
  check-then-set in C#: rejected, the race it leaves is exactly the cancel arriving during a stage.
- *The running replica notices.* `TaskCancelWatch` (in `Maf.Lab.A2A`) polls `ITaskStore.GetTaskAsync` for the running
  task every `CancelPollEvery` (default 1 s) and cancels a token linked into the run when the state is `canceled`.
  `ReviewAgentHandler.ExecuteAsync` runs its stages under that token; `TestGenerationHandler.RunOwnedAsync` links it
  into the run's token and marks the task canceled, so the run's end removes the checkpoint and the lease as a cancel
  does today. Alternative — Redis pub/sub for cancels: rejected, a second channel to keep consistent with the store,
  for a gain of under a second.
- The replica that receives the cancel goes on doing what it does: the SDK calls the handler's `CancelAsync`, which
  writes `canceled` through the store.

**Tests for it.** The guard against a real Redis (`redis:8.8.3-alpine`, the compose image, via Testcontainers' generic
`ContainerBuilder` — Testcontainers is already there through the Qdrant and Neo4j packages, so no package moves). The
two-replica behaviour with two reviewer hosts, and two test agent hosts, over one shared store: start on one, cancel
through the other, and assert the task stays `canceled`, the work stopped and (test agent) the checkpoint and lease are
gone. The in-memory store used by those host tests gets the same terminal guard through a test store, so the hosts
behave as they do over Redis.

## Risks / Trade-offs

- [The stop request fails or the runtime never ends the run] → the turn stays "Stopping…" while the run goes on, and
  ends as the run ends. Honest by design: the screen does not claim a stop the protocol did not report.
- [A future runtime emits `RUN_FINISHED { cancelled }` instead of the abort `RUN_ERROR`] → both are read.
- [An approval stopped after the server applied it shows the proposal as waiting] → answering again replays the first
  answer under the same idempotency key; nothing is applied twice.
- [Polling the store once a second per running task] → a review is tens of seconds and a test run minutes; a handful
  of reads a second for a lab's load. The interval is configurable.
- [A cancel lands between the running replica's last poll and its next stage] → that stage may start, but whatever
  it writes is refused by the store, and the next poll stops it.
- [`tasks/cancel` reaches the compliance agent after the review finished] → the agent answers with its own A2A error
  for a task that cannot be cancelled; it is recorded and ignored.
