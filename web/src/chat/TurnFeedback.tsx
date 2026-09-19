import { useReducer } from 'react';
import type { FeedbackAccepted, FeedbackKind, FeedbackRequest } from '../api/types';
import { useApi } from '../auth/useAuth';
import { feedbackReducer, feedbackStatus } from './feedbackReducer';
import styles from './TurnFeedback.module.css';

const BUTTONS: { kind: FeedbackKind; label: string }[] = [
  { kind: 'wrong_tool', label: 'Wrong tool' },
  { kind: 'wrong_document', label: 'Wrong document' },
  { kind: 'wrong_answer', label: 'Wrong answer' },
];

export function TurnFeedback({
  conversationId,
  turnId,
  initialSent = [],
}: {
  conversationId: string;
  turnId: string;
  /** Kinds already sent for this turn (restored conversations). */
  initialSent?: FeedbackKind[];
}) {
  const api = useApi();
  const [state, dispatch] = useReducer(feedbackReducer, undefined, () =>
    initialSent.length === 0
      ? {}
      : { [turnId]: Object.fromEntries(initialSent.map((k) => [k, 'sent' as const])) },
  );

  async function send(kind: FeedbackKind) {
    if (
      feedbackStatus(state, turnId, kind) !== 'idle' &&
      feedbackStatus(state, turnId, kind) !== 'error'
    ) {
      return;
    }
    dispatch({ type: 'send_started', turnId, kind });
    const body: FeedbackRequest = { conversationId, turnId, kind };
    try {
      await api<FeedbackAccepted>('/api/feedback', { method: 'POST', body });
      dispatch({ type: 'send_succeeded', turnId, kind });
    } catch {
      dispatch({ type: 'send_failed', turnId, kind });
    }
  }

  return (
    <div className={styles.row} role="group" aria-label="Feedback">
      {BUTTONS.map(({ kind, label }) => {
        const status = feedbackStatus(state, turnId, kind);
        return (
          <button
            key={kind}
            type="button"
            className={`${styles.button} ${styles[status]}`}
            disabled={status === 'sending' || status === 'sent'}
            aria-pressed={status === 'sent'}
            onClick={() => void send(kind)}
          >
            {status === 'sent' ? `✓ ${label}` : label}
            {status === 'error' && <span className={styles.errorText}> — retry</span>}
          </button>
        );
      })}
    </div>
  );
}
