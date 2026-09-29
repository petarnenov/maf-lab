# Design

## Context

- The composer is a `<textarea>` in `web/src/chat/ChatPage.tsx`. `draft` holds its text in state, and `onKeyDown`
  handles only Enter (Enter sends, Shift+Enter adds a new line).
- The conversation on screen is `state.turns` from `useChatStream`/`chatReducer`. Its `UserTurn`s carry `text`, both
  for live turns and for turns restored with `hydrate`, whose `turn.question` becomes a `UserTurn`.
- `startNew` calls `reset()`, and opening a conversation changes `routeId` and then hydrates it. Both replace
  `state.turns`.

## Goals / Non-Goals

**Goals:**
- Recall that feels like a shell's: Up steps back, Down steps forward, and Down past the end restores the draft.
- It never breaks caret movement in a multi-line draft.

**Non-Goals:**
- No history across conversations or sessions. There is no new storage, local or server side.
- No de-duplication of repeated prompts. A prompt sent twice is recalled twice, as it appears on screen.
- No history search (Ctrl+R).

## Decisions

1. **The navigation is a pure helper, `promptHistory.ts`, and the page holds its state.**
   - The helper is `step(history, state, direction) → state`, with state `{ index: number | null, saved: string }`.
     `index` is null when no recall is in progress, and `saved` is the draft at the moment the recall began.
   - The page keeps the state in a `useRef`, since changing it needs no re-render. The draft itself is set through
     `setDraft`.
   - The rules can then be unit-tested without the DOM, and `ChatPage` gains a few lines in `onKeyDown`.
   - *Alternative:* the logic inline in `onKeyDown`. Rejected: that is harder to test, and `ChatPage` is already
     475 lines.

2. **The history is derived, not stored:**
   `history = state.turns.filter(role === 'user').map(t => t.text)`.
   - It is always what is on screen, so a hydrated conversation, a new one and a failed turn's prompt are all correct
     with no extra code.

3. **When the arrows take over.** From `selectionStart`/`selectionEnd` of the textarea:
   - ArrowUp recalls only when there is no selection (`start === end`) and there is no `\n` before the caret.
   - ArrowDown steps forward only when a recall is in progress, there is no selection and there is no `\n` after the
     caret.
   - Otherwise the event is left alone.
   - Nothing happens while `e.shiftKey || e.ctrlKey || e.altKey || e.metaKey || e.nativeEvent.isComposing`. Shift+Up
     extends a selection, and composition belongs to the input method.
   - When the key is taken, `preventDefault()` runs, and after the state update the caret is moved to the end of the
     text (`setSelectionRange(len, len)` in a `requestAnimationFrame` or a layout effect).

4. **Ending the recall.**
   - `onChange` resets the state (`index = null`): an edit makes the text a new draft.
   - `submit`, `startNew` and a change of `routeId` or `state.conversationId` reset it as well.
   - The keyboard's own `setDraft` must not trigger that reset. It does not, because `onChange` fires only on user
     input, not when `value` changes programmatically.

5. **Boundaries.**
   - ArrowUp at the oldest prompt keeps the index and still calls `preventDefault()`, so the caret does not jump.
   - An empty history leaves the key alone.

## Risks / Trade-offs

- [A user who types a multi-line prompt and presses Up on its first line loses sight of their draft] → They get it
  back: Down past the newest prompt restores it exactly (`saved`).
- [A recalled multi-line prompt: Up on its first line steps to an older prompt instead of moving the caret] → This is
  shell and Slack behaviour. The caret is placed at the end, so Up first moves through the recalled text's lines.
