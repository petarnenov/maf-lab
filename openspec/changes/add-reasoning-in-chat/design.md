# Design

## Context

See proposal.md — Why. What shapes the approach:

- The reasoning events are the adapter's work, not this system's: `ChatTurnRunner` hands the agent's updates to
  `AsAGUIEventStreamAsync`, which turns `TextReasoningContent` into the `REASONING_*` events. `RunRedaction` nulls
  `RawEvent` and lets them through, so the reasoning already reaches the browser today.
- `Observed` in `ChatTurnRunner` watches the same updates on their way out, and already special-cases
  `TextContent` (into the answer and `AnswerChunker`) and an unknown `FunctionCallContent`. `TextReasoningContent`
  falls through untouched.
- `AnswerChunker` coalesces the answer into `answer.delta` trace events: flushed at 160 chars, after 150 ms,
  before a tool call and at the end of the turn, with contiguous offsets from 0.
- `TracingChatClient` accumulates `update.Text`, which in Microsoft.Extensions.AI is the text content only, so
  the `model.response` event's text is the answer and never carried the reasoning.
- On the web, `toChatEvents` is a total mapping from the protocol to this app's events and returns `[]` for the
  reasoning types. `reconstructTurn` rebuilds a turn as of a cursor from `answer.delta`, `tool.call`,
  `tool.result` and `sources`.
- `ChatPage` renders each assistant turn through `AssistantBubble`, which shows `rewound` content when the cursor
  is before the last step and the turn's own state otherwise.

## Goals / Non-Goals

**Goals:**

- The reasoning readable where it happened — in the turn — without pushing the answer off the screen.
- The same treatment the answer already gets in the trace, so a reopened turn and a rewound one both show it.
- No change to what the server streams.

**Non-Goals:**

- Sending reasoning back to the model as part of a later turn's history. `SqliteChatHistoryProvider` stores what
  it stores today; reasoning is for the person reading the screen.
- Reasoning in the review queue, the compliance trail or the evals.
- Rendering reasoning as Markdown. It is the model's scratch text and is shown as it arrived.

## Decisions

### One chunker, used twice

`AnswerChunker` becomes a chunker parameterised by the trace kind it writes and the title it gives the event
(`Answer +N chars` / `Reasoning +N chars`), and the runner keeps two instances. Its rules — 160 chars, 150 ms,
flush before a tool call and at the end — are exactly the rules the reasoning wants, and its offsets are already
per-instance.

*Alternative:* a separate `ReasoningChunker`. Rejected — it would be the same 30 lines with two constants
changed, and the flush-before-a-tool-call rule would then have to be remembered in two places.

Both chunkers flush where the answer's already does, so the interleaving in the trace stays true: reasoning that
arrived before a tool call is recorded before it.

### `reasoning.delta` is a trace kind beside `answer.delta`

`TraceKinds.ReasoningDelta = "reasoning.delta"`, data `{ offset, text }`. It inherits the trace's caps (20,000
characters per field, 1 MB per trace), retention and access scope, and the monitor's timeline shows it like any
other kind with no work. `docs/trace-events.md` gains the row.

### The client maps the protocol's reasoning events to two app events

`REASONING_MESSAGE_CONTENT` → `{ type: 'reasoning_delta', data: { text } }`, and `REASONING_END` →
`{ type: 'reasoning_end' }`, discriminated with `EventType.*` as the agui-stream spec requires. `REASONING_START`,
`REASONING_MESSAGE_START` and `REASONING_MESSAGE_END` yield nothing: the block opens on the first delta and the
run's own end already closes the turn.

`REASONING_END` is what stops the "thinking" clock. It is not what closes the block — the answer's first text is,
because a model can reason, call a tool, and reason again, and a block that closed on every `REASONING_END` would
flap.

### The turn carries the reasoning, not a separate store

`AssistantTurn` gains `reasoning: string`, `reasoningMs?: number` and `reasoningOpen?: boolean`.
`reasoning_delta` appends and, on the first delta, records when it started; `text_delta` sets `reasoningMs` the
first time answer text arrives, which is also what closes the block. `reasoningOpen` is set only by a person, and
once set it wins: the reducer never overrides it.

*Alternative:* keep the reasoning in `traceReducer` beside the frames. Rejected — the frames are a view of the
wire that belongs to the monitor, while the reasoning is part of what the turn said and belongs with its text.

### A reopened turn reads its reasoning from the trace

`ChatPage` already loads the selected turn's trace. A small `reasoningOf(events, cursor)` reads the
`reasoning.delta` events out of it, and the bubble prefers the turn's own streamed reasoning when it has one.
This is the same path the rewind uses, so a restored turn and a rewound one are one case, not two.

The consequence, stated in proposal.md — Assumptions: a restored turn shows its reasoning while its trace is
loaded, and none once the trace has expired. Storing the reasoning on the turn row instead would outlive the
7-day retention that deletes everything else about how the turn ran, which is a policy change nobody asked for.

### The block

A `<details>` with a summary, in the assistant bubble above the tool cards and the answer, so a turn reads in the
order it happened. Open state is `turn.reasoningOpen ?? (answer has not started)`. The summary reads "Thinking…"
while it runs and "Thought for 3.2 s" once the answer has started. Styled as muted, smaller text with a left rule,
so it never reads as something the assistant said.

## Risks / Trade-offs

- **A long reasoning block pushes the answer down while it streams** → it closes itself the moment the answer
  starts, which is the moment the answer becomes the thing worth reading.
- **Reasoning can be much longer than the answer, and the trace is capped at 1 MB** → it is chunked and capped
  like the answer; a trace that hits the cap marks the events it truncated, which the monitor already shows.
- **The model's reasoning can contain a document's text pulled in by retrieval** → it never reaches a log, it is
  stored in the same place under the same retention and access scope as the prompts and envelopes already in the
  trace, and it is shown only to whoever may read that turn.
- **A restored turn whose trace expired shows no reasoning** → the block is absent rather than empty, and the
  panel already says the trace expired.

## Migration Plan

Additive, with no schema change: a new trace kind and new UI. Turns traced before this change have no
`reasoning.delta` events and show no block. Rollback is removing the block and the chunker.
