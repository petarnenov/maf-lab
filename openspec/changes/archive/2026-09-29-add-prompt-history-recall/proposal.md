# Proposal

## Why

Asking the same or a slightly different question again means typing it out again: the chat input has no memory of what
the user already asked. Terminals and chat tools have taught people to press ArrowUp for that.

## What Changes

- **ArrowUp recalls the previous prompt.** In the chat input, ArrowUp puts the previous prompt the user sent in this
  conversation into the input. Each further press steps one prompt further back. At the oldest prompt, the input stays
  where it is.
- **ArrowDown steps forward again.** Past the newest prompt, it restores what the user had typed before they started
  recalling.
- **Normal cursor movement keeps working.** Recall takes over the key only when the caret is on the first line (for
  ArrowUp) or the last line (for ArrowDown) with no text selected. It also does nothing while a modifier is held or an
  input method is composing.
- **The history is what is on screen.** The user prompts of the conversation on screen, oldest to newest, form the
  history. This includes prompts restored from a stored conversation. Sending, starting a new conversation or opening
  another one ends a recall.
- **Editing ends the recall.** Typing into a recalled prompt makes that text the new draft, and the next ArrowUp starts
  again from the newest prompt.
- Nothing is sent or stored: the feature reads only the turns the page already holds.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `web-ui`: adds keyboard recall of earlier prompts in the chat input.

## Impact

- Code: `web/src/chat/ChatPage.tsx`, where the composer's `onKeyDown` gains ArrowUp and ArrowDown handling. It uses a
  small pure helper for the navigation, with its own unit tests.
- Tests: `web/src/chat/` Vitest cases for recall, forward, restoring the draft, boundaries, multi-line prompts, and
  resets on send and on a conversation switch.
- API, server and storage: none. Dependencies: none added.
