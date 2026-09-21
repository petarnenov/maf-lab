# Design

## Context

When a user clicks Approve or Reject on a `ConfirmationCard`, `useChatStream.answer()` fires a POST to
`/api/chat` with a `resume` payload. Today that function reads the response body into a local `said` string
(via a private accumulator in the `readChatStream` callback) and then dispatches a single `answered` action.
This bypasses the normal `send()` path, which:

1. dispatches `{ type: 'send' }` → sets `streaming: true` + adds a streaming assistant turn
2. pipes every SSE event through `dispatch({ type: 'event', event })`
3. dispatches `{ type: 'event', done }` → sets `streaming: false`

Because `answer()` skips step 1 and 2, `state.streaming` stays `false` throughout, no streaming turn
is added, and the `MonitorPanel` receives an empty `events` array for the new turn. The panel either
stays on the original proposal turn (whose trace is already complete) or renders a blank state. The
`said` string is then used to construct a static turn added via `answered` — with no trace at all.

See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- The resume stream flows through the same `readChatStream` → `dispatch({ type: 'event' })` pipeline as `send()`
- `state.streaming` is `true` while the resume run is in flight, making the monitor panel follow the answer turn
- The `answered` action is narrowed to settling the confirmation state; turn creation is handled by the pipeline
- All existing `ChatPage.confirmation.test.tsx` scenarios continue to pass
- A new scenario verifies the monitor panel stays open and receives events during the answer run

**Non-Goals:**
- No changes to the backend or the SSE wire format
- No changes to `ConfirmationCard` or its appearance
- The `send()` path and its unrelated callers are not touched

## Decisions

### 1. Reuse `send()`'s event pipeline in `answer()`

**Decision**: Mirror the `send()` implementation exactly:
- dispatch `{ type: 'send', ... }` (or an equivalent action) before the POST to push a streaming turn and set `streaming: true`
- call `readChatStream(body, event => dispatch({ type: 'event', event }))` to pipe all SSE events
- dispatch nothing extra at the end — the `done` event from the stream already settles the turn

The `answered` action is then reduced to *only* updating `confirmationState` on the matching turn (no new
turn creation, no `said` text). The run's reply arrives naturally via `text_delta` events.

**Alternative considered — keep `said` shortcut, just add `streaming: true`**: Adding `streaming: true` alone
would keep the monitor open but still produce a blank trace and miss tool-call / source events. Rejected.

**Alternative considered — separate `answerTurnId` action**: A new `start_answer` action could distinguish
the answer turn from a normal user→assistant exchange. The added complexity is not justified; the `send`
pipeline already handles all turn lifecycle.

### 2. Keep `answered` but remove `said` from it

The `said` field was the only reason `answered` wrote a new turn. Once events flow through the pipeline, `said`
is both redundant and wrong (the monitor would see a second, traceless turn). Remove `said` from the action
shape. The `outcomeOf` helper is still needed to produce the `outcome` value dispatched in `answered`.

### 3. `answer()` must know the `adjustmentId` to pass to `answered`

`answer()` already receives `adjustmentId` as its first argument — no change needed there.

### 4. `ChatPage` wiring

`ChatPage.tsx` passes `answer` (from `useChatStream`) directly as `onAnswer` on `AssistantBubble`. Since
`answer()` now calls `dispatch({ type: 'send' })`, `state.streaming` becomes `true`, which automatically:
- disables the Send button (already guarded by `state.streaming`)
- keeps the `MonitorPanel` visible (`monitorOpen` is untouched by the fix)
- makes `selected` follow the new streaming turn (same logic as a normal turn)

No changes to `ChatPage.tsx` are expected to be necessary.

## Risks / Trade-offs

- **The answer turn appears as a normal assistant turn (no special "resume" marker)**: The text the model
  returns already says "Applied" or "Nothing was applied", so the user always knows the outcome. Accepted.
- **`said` text no longer accumulates separately**: `outcomeOf(said, approve)` must still work; a small
  adapter is needed to collect text events for outcome detection while also passing them to the reducer.
  Risk is low — `readChatStream` already accumulates via its callback; the adapter is a one-liner closure.

## Migration Plan

Frontend-only change. No deploy coordination required. The change is safe to ship directly; the backend
contract (`POST /api/chat` with `resume`) is unchanged.
