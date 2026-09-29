/**
 * Recalling earlier prompts in the chat input with the arrow keys (add-prompt-history-recall), as a shell does: Up
 * steps back through what the user sent, Down steps forward, and Down past the newest prompt restores the draft the
 * user had before the recall began. Pure: the page keeps the state and applies the text.
 */

/** Where a recall stands: the history index shown (null when not recalling) and the draft it started from. */
export interface RecallState {
  index: number | null;
  saved: string;
}

export const idle: RecallState = { index: null, saved: '' };

/**
 * One arrow press. Returns the new state and the text the input should show, or null when the key is not the
 * recall's to take (no history, or Down with no recall in progress) and should do what it normally does.
 */
export function step(
  history: readonly string[],
  state: RecallState,
  direction: 'up' | 'down',
  draft: string,
): { state: RecallState; text: string } | null {
  if (direction === 'up') {
    if (history.length === 0) return null;
    if (state.index === null) {
      const index = history.length - 1;
      return { state: { index, saved: draft }, text: history[index] };
    }
    // At the oldest prompt the key is still taken, so the caret does not jump; the input stays as it is.
    const index = Math.max(0, state.index - 1);
    return { state: { ...state, index }, text: history[index] };
  }
  if (state.index === null) return null;
  if (state.index < history.length - 1) {
    const index = state.index + 1;
    return { state: { ...state, index }, text: history[index] };
  }
  return { state: idle, text: state.saved };
}
