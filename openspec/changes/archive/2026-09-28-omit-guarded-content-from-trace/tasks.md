# Tasks

## 1. Blocked prompt skips the prompt trace and the tool fetch

- [x] 1.1 In `ChatTurnRunner.RunAsync`, classify the intent and screen the prompt before fetching tools, then gate the tool fetch (`GetToolsAsync`), the chat-options build, the `prompt` trace event and the agent run behind `!screen.Blocked`; a blocked turn produces the fixed refusal with none of that work. Keep the intent and `guardrail` trace events, `forced`/`route`, and the stream-consume loop producing an identical benign trace.
- [x] 1.2 Verify a benign (procedural) turn's trace is unchanged — same events in the same order, `prompt` still carries the system prompt and four tool schemas — via the existing ordering test.

## 2. Withheld tool-result content stays out of the trace

- [x] 2.1 In `ChatTurnRunner.InvokeToolAsync`, screen the tool result before `TraceToolResult`, and record the redacted result (the neutral notice + count from the screening) in the `tool.result` event when anything was withheld; keep the raw result for a clean turn and keep the retrieval diagnostics and serving replica.
- [x] 2.2 Update the stale `Guardrail.Trace` comment that claimed the raw tool result always holds the assessed text.

## 3. Audit the reviewer and A2A partner sites

- [x] 3.1 Confirm a flagged reviewer's words are not traced (screening precedes the `reviewed` event; a flagged verdict becomes the neutral failed outcome). No code change needed.
- [x] 3.2 Confirm a blocked A2A partner prompt refuses before any tool fetch or prompt build and writes no turn trace. No code change needed.

## 4. Regression tests

- [x] 4.1 A guardrail-blocked turn's streamed trace does not contain the `prompt` event (nor history nor envelope), while the refusal still streams and `guardrail_blocked` is signalled.
- [x] 4.2 The persisted `TurnTraces` row of a blocked turn contains no system prompt text; a benign turn's stored trace still does.
- [x] 4.3 A withheld search excerpt's text appears in neither the `tool.result` trace event nor the envelope event, while the clean excerpt is delivered and `guardrail_withheld` is signalled.

## 5. Verify

- [x] 5.1 `make lint` and `make test` pass; `openspec validate omit-guarded-content-from-trace --strict` and `make specs` pass.
