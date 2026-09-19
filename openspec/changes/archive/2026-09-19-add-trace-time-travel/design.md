# Design

## Context

See proposal.md. Current code:
- Web: `MonitorPanel` receives `events: TraceEvent[]` (live from `traceReducer`, or stored via `useTurnTrace`) and renders
  tabs in `MonitorTabs.tsx`. `ChatPage` renders turns from `chatReducer` state (text, tool cards, sources) and passes
  the selected turn's events to the monitor.
- API: `ChatTurnRunner` appends `TextContent` deltas to the answer and writes `text_delta` SSE events. The trace has
  no answer text except the `model.response` totals.

## Goals / Non-Goals

**Goals:**
- A pure, testable time-travel model: every view is a function of `(events, cursor)`.
- Exact chat reconstruction for the selected turn at any step.
- Smooth live behaviour: follow the head, pause on user interaction.

**Non-Goals:**
- Time travel across turns or whole conversations (one turn at a time; turns are already selectable).
- Server-side replay or re-execution of a turn.
- Persisting the cursor in the URL.

## Decisions

### D1. `answer.delta` coalescing (API)
`ChatTurnRunner` appends each `TextContent` delta to a pending buffer and flushes it as a trace event
`answer.delta { offset, text }` (title "Answer +N chars") when:
- the buffer reaches 160 characters, or
- 150 ms have passed since the last flush, or
- a tool call starts or the stream ends.

Offsets are cumulative. Concatenating the chunks equals the answer (tested). Coalescing keeps the trace small: a
1,200-character answer is roughly 8–15 events instead of hundreds. The SSE `text_delta` stream is unchanged.

### D2. Time-travel state (web)
`timeTravelReducer`, pure: `{ cursor: number | "live", playing, speed, compressWaits }`, with the actions `seek(n)`,
`step(±1)`, `start`, `end`, `play`, `pause`, `tick`, `setSpeed`, `toggleCompress`, `goLive` and `eventsChanged(count)`.
- `cursor = "live"` means following the head. Any manual seek or step sets a number and stops following.
- `goLive` restores following.
- `effectiveCursor(state, count)` resolves `"live"` to `count` (0 = before the first event).

The `usePlayback` hook drives `tick` with `setTimeout`, using the gap to the next event's `atMs` divided by the speed.
With `compressWaits`, gaps are capped at 1,000 ms before scaling. Playback stops at the end. For stored turns the
monitor opens at the end (the full picture); for live turns it follows.

### D3. Step-filtered views
`MonitorPanel` computes `visible = events.filter(e => e.seq <= cursor)` and `current = events[cursor-1]`, and passes
`visible` to every tab (the tabs already derive everything from the events they receive).
- **Timeline:** shows all events, dims the future ones, highlights and scrolls to the current row; clicking a row seeks.
- **Model tab:** a request without its response shows "waiting for response…".
- **"This step":** a panel above the tabs with kind, title, `atMs`, duration and a `JsonView` of the data.

### D4. Chat reconstruction
`reconstructTurn(events, cursor)` is a pure function that returns `{ text, toolCards, sources, stepLabel }` from the
visible events:
- `text` is the concatenation of `answer.delta` chunks;
- `toolCards` comes from `tool.call` and `tool.result` (running/finished, with the same summaries the live cards use);
- `sources` comes from the `sources` event.

`ChatPage` uses it for the selected turn when the cursor is below the event count; otherwise the turn renders from
`chatReducer` as today. A banner "⏪ Viewing step k of N — Return to now" sits above the turn. Traces stored before
this change have no `answer.delta` events: the text then falls back to the final answer, and the banner says "answer
text not recorded for this turn".

### D5. Keyboard and accessibility
Key handling is scoped to the monitor region (focusable, `aria-keyshortcuts`), so typing in the chat input never
scrubs. The scrubber is a native `<input type="range">` with `aria-valuetext` "step k of N: kind — title". Buttons have
labels. Play and pause announce through a polite live region.

### D6. Review queue
The review queue uses the same `MonitorPanel`, so time travel works there without chat reconstruction.

## Risks / Trade-offs

- [Older stored traces have no `answer.delta`] → graceful fallback (D4). They expire within 7 days anyway.
- [Long model latencies make 1× replays slow] → speeds and compressed waits (on by default).
- [Re-rendering large traces on every tick] → the views are memoised on `(events, cursor)`, and `JsonView` renders
  lazily, only when expanded.
