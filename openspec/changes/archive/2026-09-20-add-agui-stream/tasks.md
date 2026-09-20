# Tasks

## 1. The protocol in the solution

- [x] 1.1 Pin `AGUI.Server` and `AGUI.Abstractions` 1.0.0 in `Directory.Packages.props` and reference them from `Maf.Lab.Api`; verify the solution builds with warnings as errors and that `Microsoft.Extensions.AI.Abstractions` still resolves to the pinned 10.10.0
- [x] 1.2 A run's identity and addressing: the request shape (`RunAgentInput`) accepted at the chat endpoint, the thread resolved to a conversation owned by the caller, a new thread created when none is given; verify tests: a run without a thread creates one and reports it, another principal's thread is not found, and every event of a run carries its run and thread ids

## 2. The stream

- [x] 2.1 An AG-UI writer in `src/Maf.Lab.Api/Agent/`: the channel carries `BaseEvent`, the endpoint names each SSE frame by the event's own type, and `ChatEvent`'s invented hierarchy is removed from `Maf.Lab.Domain/Chat/ChatEvents.cs` (the request and conversation DTOs stay); verify tests: each frame's name matches the event's discriminator and the payload round-trips through the protocol's serializer
- [x] 2.2 The model's output through the adapter: convert the agent stream with `AsChatResponseUpdatesAsync` and enumerate `AsAGUIEventStreamAsync`, writing its events into the same channel in order; verify tests: a streamed answer produces one text-message start, its content in order under one message id, and one end — and a turn with no answer opens no message
- [x] 2.3 Run lifecycle: run-started first, exactly one terminal event, nothing after it, and a failure ending as run-error with short user-facing text; verify tests: a complete run's first and last events, a failing turn's terminal event, and that no event follows the terminal one
- [x] 2.4 Tool calls from the middleware as the protocol's four events under one tool-call id, the start before the tool executes, arguments and results carrying identifiers and summaries only; verify tests: the lifecycle order, and that a query and a reason do not appear in what the client receives

## 3. What the protocol does not name

- [x] 3.1 Sources and the trace as custom events under `maf-lab/sources` and `maf-lab/trace`, carrying the payloads they carry today; verify tests: the sources arrive before the terminal event, trace events interleave and all arrive before it, and the stored trace is unchanged
- [x] 3.2 The web client maps the protocol to the existing reducer actions, routing custom events by name and ignoring anything unrecognised; verify Vitest: a full recorded run drives the reducer to the same state as today, an unknown event is ignored, and the monitor and time-travel tests pass unchanged

## 4. Pausing for a person

- [x] 4.1 A waiting proposal becomes an interrupt on the terminal event — id, message, response schema, tool call id, expiry, and the summary and state as metadata — replacing `ConfirmationRequiredEvent`; verify tests: a proposal pauses the run with every field populated, nothing is written, and no tool runs afterwards
- [x] 4.2 The proposal's expiry reaches the interrupt: the signer exposes when a proposal stops being answerable and the tool carries it through; verify a test that the interrupt's expiry matches the state's
- [x] 4.3 Resuming: a run carrying a resume for an interrupt routes to the confirmation path, approving applies and declining applies nothing, and the run reports what happened; delete `POST /api/chat/confirm`; verify tests: approve applies once, decline applies nothing, a second answer to the same interrupt is refused, and another user's interrupt is refused

## 5. Stopping a run

- [x] 5.1 A run registry per instance and a stop request that cancels it, reaching the instance that owns the run by asking the service's other replicas when this one does not; the run ends reporting that it was cancelled; verify tests: a stop ends the run within a second, no tool executes after it, a stop for a run on another replica finds it, and one asked to stay local does not fan out
- [x] 5.2 Abandoning the stream stops the run through the request's own cancellation; verify a test that dropping the client ends the turn rather than running it to completion

## 6. Everything that reads the stream

- [x] 6.1 The eval harness host consumes the new run; verify `EvalHarnessTests` passes and one suite runs end to end against the stack
- [x] 6.2 `scripts/verify_lb.sh`: the SSE order check moves to the protocol's events and a run through the balancer still streams incrementally; verify `make verify` passes
- [x] 6.3 Run `make test`, `make lint`, `COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`

## 7. Verification and docs

- [x] 7.1 Live: bring the stack up, ask a question, watch a tool call, propose an adjustment and see the run pause, resume it to apply, resume another to decline, and stop a run mid-flight — record what the stack showed
- [x] 7.2 Re-run the evals whose inputs moved (`selection`, `injection`) and read the regression comparison; accept the baseline deliberately or fix what moved
- [x] 7.3 Docs: `docs/http-api.md` (the run request, the event stream, resuming, stopping), `docs/trace-events.md` (the trace as a custom event), README (what the browser and the API now speak), `DECISIONS.md` §26 — the packages, the adapter-inside-the-pipeline choice and the two rejected alternatives, the interrupt over a bespoke event, why `/api/chat/confirm` was deleted rather than kept, and that a stop only reaches the replica running the turn; verify the sections exist
