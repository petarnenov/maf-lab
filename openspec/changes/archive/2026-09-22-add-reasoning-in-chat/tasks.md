# Tasks

## 1. Recording the reasoning in the trace

- [x] 1.1 Add `TraceKinds.ReasoningDelta = "reasoning.delta"` to `src/Maf.Lab.Domain/Tracing/TraceEvent.cs`; verify
      the solution builds with `make lint-dotnet`.
- [x] 1.2 Parameterise `AnswerChunker` by the trace kind and event title it writes, keeping its 160-char / 150-ms /
      flush-before-a-tool-call rules and its per-instance offsets; verify the existing `answer.delta` assertions in
      `TurnTraceTests` still pass unchanged.
- [x] 1.3 Feed `TextReasoningContent` from `ChatTurnRunner.Observed` into a second chunker and flush it wherever the
      answer's is flushed; verify a new xUnit case shows a reasoning turn's `reasoning.delta` events have contiguous
      offsets from 0, concatenate to what the model reasoned, and sit before the `model.response` that followed.
- [x] 1.4 Verify a turn whose model does not reason records no `reasoning.delta`, and that no log line carries the
      reasoning, with a test that answers without reasoning and one that reasons with a marker string.

## 2. The reasoning on the wire, in the client

- [x] 2.1 Map `EventType.REASONING_MESSAGE_CONTENT` to `{ type: 'reasoning_delta' }` and `EventType.REASONING_END`
      to `{ type: 'reasoning_end' }` in `web/src/chat/chatEvents.ts`, leaving the other three reasoning types
      yielding nothing; verify new cases in `chatEvents.test.ts` cover all five types.
- [x] 2.2 Add `reasoning`, `reasoningMs` and `reasoningOpen` to `AssistantTurn` and handle the two events in
      `chatReducer`: a delta appends and starts the clock, `reasoning_end` stops it, the first `text_delta` sets
      `reasoningMs` and closes the block; verify `chatReducer.test.ts` covers a turn that reasons, answers, then
      reasons again without the block reopening.
- [x] 2.3 Add an `evals/ui-events.jsonl` recording of a run that reasons (or extend an existing one's expectations
      with its reasoning) and assert the replayed reasoning in `recordedRuns.test.ts`; verify the replay reports the
      reasoning the recorded run actually carried.

## 3. The block in the chat

- [x] 3.1 Render a `ReasoningBlock` in `AssistantBubble` above the tool cards — a `<details>` whose summary reads
      "Thinking…" while it runs and "Thought for N s" once the answer started, open per
      `reasoningOpen ?? (no answer yet)`; verify a new `ChatPage` test covers streaming open, auto-collapse on the
      first answer text, and no block for a turn that did not reason.
- [x] 3.2 Keep a person's open/close choice for the rest of the turn by setting `reasoningOpen` on toggle; verify a
      test opens the block after it collapsed and asserts it stays open as more answer text arrives.
- [x] 3.3 Style the block in `ChatPage.module.css` as muted, smaller text with a left rule so it never reads as the
      assistant's answer; verify it holds up at the chat pane's narrow width.

## 4. A reopened turn and a rewound one

- [x] 4.1 Add `reasoning` to `ReconstructedTurn` in `web/src/monitor/reconstructTurn.ts`, built from the
      `reasoning.delta` events up to the cursor, open while the cursor is still among them; verify
      `reconstructTurn.test.ts` covers a cursor inside the reasoning and one after the answer started.
- [x] 4.2 Pass the selected turn's stored reasoning from `ChatPage` to the bubble, preferring the turn's own
      streamed reasoning when it has one; verify a `ChatPage.history` test shows a reopened turn's reasoning from
      its stored trace, collapsed, and none when the trace has expired.

## 5. Documentation and verification

- [x] 5.1 Document the `reasoning.delta` kind in `docs/trace-events.md`, including its place in the typical order;
      verify the documented shape matches what the trace records.
- [x] 5.2 Run `make lint` and `make test` and confirm they pass.
- [x] 5.3 Run the stack (`make`), ask a question, and confirm the block streams and then collapses to "Thought
      for N s"; reload the conversation and confirm the reasoning comes back from the stored trace.
