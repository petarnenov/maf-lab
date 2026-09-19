# Proposal

## Why

The behind-the-scenes monitor shows a finished turn all at once, or a live turn as it streams past. Understanding
*how* the agent got to its answer means seeing each step in isolation: what the model had been sent before it chose a
tool, what retrieval returned before the envelope was built, how much of the answer existed when a tool finished.
Time travel over the recorded trace makes every turn replayable, step by step, at any speed. That turns the monitor from
a log into a debugger.

## What Changes

- **Time-travel controls in the monitor:**
  - a scrubber over the turn's trace steps;
  - step back and forward, jump to start and end;
  - play and pause at the recorded timing with 1×, 2×, 5× and 10× speeds, and an option to compress long waits;
  - keyboard shortcuts;
  - "Back to live" while a turn is streaming.
- **Every monitor tab renders the state as of the selected step:**
  - only events up to that step are shown;
  - the current step is highlighted;
  - a "this step" panel shows what the step added.
- **The chat rewinds with it:** for the selected turn, the answer text, the tool cards (running vs finished) and the
  sources appear exactly as they were at that step, with a visible "viewing step k of N" banner and a one-click return.
  Other turns are unaffected.
- **Backend:** the turn trace also records the streamed answer text as coalesced `answer.delta` events, so the chat can
  be reconstructed at any step. This is a small change in `ChatTurnRunner` and the trace contract.
- Works for live turns, stored turns, and traces opened from the review queue (monitor only, there is no chat pane).

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `turn-tracing`: "Complete turn trace" also covers the streamed answer text, as ordered chunks.
- `web-ui`: new requirements for time-travel controls, for monitor views as of a step, and for chat reconstruction
  as of a step.

## Impact

- API: `answer.delta` trace events coalesced from text deltas (no extra SSE event types; the existing `text_delta`
  events are unchanged). Docs: `docs/trace-events.md`.
- Web: a time-travel state (cursor, playing, speed, follow-live) in the monitor, a `TimeTravelBar` component,
  step-filtered views, chat reconstruction for the selected turn, and keyboard handling. Tests.
- No new packages, and no storage changes (traces are already stored).
