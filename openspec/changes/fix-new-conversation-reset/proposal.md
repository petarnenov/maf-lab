# Proposal

## Why

"New conversation" does not start a new conversation. With a stored conversation open, clicking it navigates to
`/chat` and is then thrown back: the browser history shows `push /chat` followed ~30 ms later by
`replace /chat/{old-id}`, and the old turns are still on screen. The behaviour is already required
(`chat-history` → "History in the chat screen" → *New conversation*), so the screen contradicts its own spec, and the
only way to start fresh today is to reload the page.

The cause is a race between two effects in `ChatPage`. `startNew()` clears the state and navigates, but react-router
applies the navigation one tick later. In that tick the state is empty while the route still points at the old
conversation, so the "load the conversation in the URL" effect sees work to do, finds the answer already in the
TanStack Query cache, and hydrates the old conversation back. When the route finally becomes `/chat`, the state has
an id again — and the effect whose job is to put a *newly created* conversation's id into the URL sends the user
back to the old one.

## What Changes

- Clicking "New conversation" SHALL leave the user on an empty `/chat`, whatever is cached, whether the conversation
  was opened from the sidebar or restored by a reload.
- The id is put into the URL only when a conversation was *just created by sending a message*, instead of whenever
  the state happens to hold an id the route does not.
- A conversation is not re-hydrated while the route is moving away from it.
- Regression tests at the level the bug lives: the page with a primed query cache, not the sidebar in isolation —
  the existing sidebar test passes because it only asserts that the click calls its handler.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `chat-history`: "History in the chat screen" gains a scenario pinning that starting a new conversation is not
  undone by a cached conversation, so the regression cannot come back unnoticed.

## Impact

- `web/src/chat/ChatPage.tsx` — the two effects and `startNew`.
- `web/src/chat/ChatPage.history.test.tsx` — regression coverage.
- No API, storage or contract change; no other screen is affected.
