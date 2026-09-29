# Tasks

## 1. Navigation helper

- [x] 1.1 Add `web/src/chat/promptHistory.ts` with a pure `step(history, state, 'up' | 'down')` (design §1, §5) and `promptHistory.test.ts`. Verify with Vitest:
  - Up steps back and stops at the oldest prompt.
  - Down steps forward, and past the newest prompt restores the saved draft (an empty draft included).
  - An empty history is a no-op.
  - Down with no recall in progress is a no-op.

## 2. Composer

- [x] 2.1 In `ChatPage.tsx`, derive the history from `state.turns` and handle ArrowUp/ArrowDown in the textarea's `onKeyDown`:
  - apply the first-line/last-line and no-selection rule, and the modifier/`isComposing` guard;
  - call `preventDefault` only when the key is taken;
  - place the caret at the end after a recall.

  Verify with `ChatPage` tests: stepping back three prompts, the oldest limit, restoring the draft, no prompts, a
  two-line draft whose caret moves instead, and Shift+ArrowUp not recalling.
- [x] 2.2 End the recall on edit (`onChange`), on `submit`, on `startNew` and on a conversation switch. Verify with tests:
  - editing a recalled prompt then pressing ArrowUp shows the newest prompt, and Down restores the edit;
  - after sending, ArrowUp shows the prompt just sent;
  - a hydrated stored conversation recalls its last question.

## 3. Verification

- [x] 3.1 Run `make test-web` and `make lint`, then check in the running app (`make`, http://localhost:7171/chat) that ArrowUp/ArrowDown recall and restore as specified; all green
- [x] 3.2 Run `openspec validate add-prompt-history-recall --strict`; valid
