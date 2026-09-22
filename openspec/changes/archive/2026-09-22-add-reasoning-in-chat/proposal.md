# Proposal

## Why

`gpt-oss:120b` reasons before it answers, and that reasoning already crosses the wire: a live run carries
`REASONING_START`, `REASONING_MESSAGE_START`, a `REASONING_MESSAGE_CONTENT` per delta, `REASONING_MESSAGE_END`
and `REASONING_END`. The web client discards every one of them — `toChatEvents` returns `[]` for anything it has
no case for — so the screen shows "Thinking…" while the most interesting part of the turn goes past unseen. The
trace does not record it either: `TracingChatClient` follows `update.Text`, which is the answer's text content and
not the reasoning, so nothing about how the model got there survives the turn.

## What Changes

- The assistant bubble gains a reasoning block: it opens and streams while the model is thinking, and collapses
  by itself the moment the answer's first text arrives, leaving a summary line ("Thought for 3.2 s") that reopens
  on a click.
- The turn's trace records the reasoning the way it already records the answer: coalesced `reasoning.delta`
  events with contiguous offsets, so concatenating them yields exactly what the model reasoned.
- A reopened turn shows its reasoning from that stored trace, and rewinding the trace rewinds the reasoning
  along with the answer, so a step before the model finished thinking shows only what it had thought by then.
- The model's reasoning is message content: it stays out of the logs, lives under the trace's retention and
  access scope, and is never sent to the model as part of a later turn's history.

## Capabilities

### New Capabilities

None. The behavior extends what `turn-tracing` already records and `web-ui` already shows.

### Modified Capabilities

- `turn-tracing`: the complete-turn-trace requirement gains the model's reasoning, recorded as ordered chunks the
  way the streamed answer already is.
- `web-ui`: a new requirement for the reasoning block in the chat, and the chat-as-of-a-step requirement gains the
  reasoning reconstructed from `reasoning.delta`.

## Impact

- `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs` — `Observed` sees `TextReasoningContent` and feeds it to a chunker.
- `src/Maf.Lab.Api/Agent/Tracing/AnswerChunker.cs` — one chunker, used for the answer and for the reasoning.
- `src/Maf.Lab.Domain/Tracing/TraceEvent.cs` — a `reasoning.delta` kind.
- `web/src/chat/chatEvents.ts`, `chatReducer.ts`, `ChatPage.tsx`, `ChatPage.module.css` — the events, the turn's
  reasoning state and the block that shows it.
- `web/src/monitor/reconstructTurn.ts` — the reasoning as of a step.
- `docs/trace-events.md` — the new kind.
- No change to what the server streams: the reasoning events were already going out.

## Assumptions

- Reasoning is kept only in the turn's trace, not on the turn row beside the question and the answer. A turn row
  has no retention; putting reasoning there would keep it after the 7-day trace retention has deleted everything
  else about how the turn ran. A reopened turn therefore shows its reasoning while its trace is loaded — which is
  the turn the monitor is showing — and a turn whose trace has expired shows none.
