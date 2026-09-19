import type { FeedbackKind } from '../api/types';

export type FeedbackStatus = 'idle' | 'sending' | 'sent' | 'error';

/** Status per turn, per feedback kind. Missing entries are 'idle'. */
export type FeedbackState = Record<string, Partial<Record<FeedbackKind, FeedbackStatus>>>;

export type FeedbackAction =
  | { type: 'send_started'; turnId: string; kind: FeedbackKind }
  | { type: 'send_succeeded'; turnId: string; kind: FeedbackKind }
  | { type: 'send_failed'; turnId: string; kind: FeedbackKind };

export function feedbackReducer(state: FeedbackState, action: FeedbackAction): FeedbackState {
  const current = feedbackStatus(state, action.turnId, action.kind);
  let next: FeedbackStatus;
  switch (action.type) {
    case 'send_started':
      // Sending twice, or re-sending once accepted, is a no-op.
      if (current === 'sending' || current === 'sent') return state;
      next = 'sending';
      break;
    case 'send_succeeded':
      if (current !== 'sending') return state;
      next = 'sent';
      break;
    case 'send_failed':
      if (current !== 'sending') return state;
      next = 'error';
      break;
  }
  return { ...state, [action.turnId]: { ...state[action.turnId], [action.kind]: next } };
}

export function feedbackStatus(state: FeedbackState, turnId: string, kind: FeedbackKind) {
  return state[turnId]?.[kind] ?? 'idle';
}
