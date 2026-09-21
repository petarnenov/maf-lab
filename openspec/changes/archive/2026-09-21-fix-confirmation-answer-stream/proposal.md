# Proposal

## Why

When the advisor clicks Approve or Reject on a fee-adjustment proposal, the resume POST is processed through a
local text accumulator in `useChatStream.answer()` instead of the normal `readChatStream` → reducer event
pipeline. As a result: the monitor panel (Behind the scenes) disappears or goes blank immediately after the
button is clicked, no streaming turn is shown while the answer arrives, and the new assistant turn added from
`action.said` has no trace and no tool-call events — the same loss of context that the behind-the-scenes
monitor exists to prevent.

## What Changes

- **`web/src/chat/useChatStream.ts` — `answer()`**: route the resume stream through the existing
  `readChatStream` / `dispatch({ type: 'event' })` pipeline, just as `send()` does, rather than
  consuming it with a private accumulator.
- **`web/src/chat/chatReducer.ts` — `answered` action**: the new assistant turn for the run's reply
  currently materialises only from `action.said`; once events flow through the pipeline the reducer
  must handle them as a normal streaming turn. The `said` shortcut is removed; `answered` settles the
  confirmation state only.
- **`web/src/chat/ChatPage.tsx`**: the `answer` callback must set `streaming: true` for the duration
  of the resume run so the monitor panel stays open and follows the answer turn (same `monitorOpen`
  logic that `send` already triggers).
- **Tests**: `ChatPage.confirmation.test.tsx` has existing passing scenarios for Approve/Reject but
  does not assert that the monitor panel remains open or that the tool call / trace events are
  received. New assertions are needed; existing ones must continue to pass.

## Capabilities

### New Capabilities

*(none — this is a bug fix)*

### Modified Capabilities

- `write-confirmation-ui`: The requirement "Approving and rejecting are the person's two answers"
  currently says the answer run *reports the outcome in the conversation* but says nothing about how
  the monitor behaves during that run. A new scenario is needed: while an answer is streaming the
  Behind-the-scenes panel SHALL stay visible and follow the answer turn, consistent with how every
  other run is handled.

## Impact

- `web/src/chat/useChatStream.ts` — `answer()` function only
- `web/src/chat/chatReducer.ts` — `answered` case and the `ChatAction` for it
- `web/src/chat/ChatPage.tsx` — `answer` callback wiring (state.streaming guard already disables Send)
- `web/src/chat/ChatPage.confirmation.test.tsx` — new assertions; all existing scenarios unaffected
- No backend changes; no API contract changes; no eval impact
