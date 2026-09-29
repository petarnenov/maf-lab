# Proposal

## Why

When a chat turn fails (the server ends the run with an AG-UI `RUN_ERROR`), the person sees their own question and
nothing under it — no answer and no error. The server does send the error text; the chat screen throws it away. Seen
live on 2026-09-29 while the api ran without `OLLAMA_API_KEY`: every model-backed turn failed and every one looked
like the assistant simply ignored the question.

Cause, traced in the web app: a `RUN_ERROR` becomes the stream's `done` event with an empty conversation id; the reducer
writes that empty id over the conversation on screen; the chat page then sees the route's conversation id differ from
the one in state, reloads the stored conversation and hydrates it over the live turns. The stored turn carries no
error, so the error the run ended with disappears.

## What Changes

- A run that ends in an error keeps the conversation it belongs to: a terminal event that names no conversation does not
  change which conversation is on screen.
- The chat screen learns the conversation from the start of the run (AG-UI `RUN_STARTED` names the thread), so a
  first turn that fails still belongs to its conversation and the next message continues it.
- The failed turn therefore stays as it was streamed, and shows the run's error (the server's generic text, never an
  internal one) with the existing "unavailable / try again" treatment.
- Regression tests at the reducer and at the chat page for a run that ends in `RUN_ERROR`.

Not in scope: a failed turn in a conversation reopened from history still shows no error, because history does not
store failures. That is a separate change (it touches the stored turn shape).

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `web-ui`: adds a requirement that a turn which fails stays on screen with its error, and does not reload or replace the
  conversation.

## Impact

- `web/src/chat/chatReducer.ts` (the `done` case), `web/src/chat/chatEvents.ts` (`RUN_STARTED` mapping), `web/src/api/types.ts` (stream event union).
- Tests: `web/src/chat/chatReducer.test.ts`, a chat page test for the failed-run path.
- No server, API or data change.
