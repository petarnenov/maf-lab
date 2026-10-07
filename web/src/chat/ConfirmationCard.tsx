import type { PendingWrite } from '../api/types';
import { usePlugins } from '../plugins/context';
import { PluginBoundary } from '../plugins/PluginBoundary';
import { confirmationRenderers } from '../plugins/registry';
import type { ConfirmationState } from './chatReducer';
import styles from './ConfirmationCard.module.css';
import { WriteSummary } from './WriteSummary';

interface Props {
  confirmation: PendingWrite;
  state: ConfirmationState;
  onAnswer: (approve: boolean) => void;
}

/**
 * A write waiting for the advisor, in the conversation where it was proposed rather than over it. It shows what the
 * server would do — through the write's plugin's renderer for its tool when it has one, otherwise from the summary's
 * schema (generalize-write-confirmation) — and offers the two answers. Once it has been answered, or is too late to
 * answer, it says so instead of asking again.
 */
export function ConfirmationCard({ confirmation, state, onAnswer }: Props) {
  const plugins = usePlugins();
  const contributed = confirmationRenderers(plugins)[confirmation.toolName];
  const answerable = state === 'waiting' || state === 'answering';

  return (
    <div
      className={`${styles.card} ${styles[state]}`}
      data-testid="confirmation-card"
      data-state={state}
    >
      <p className={styles.question}>{confirmation.question}</p>

      {contributed ? (
        <PluginBoundary plugin={contributed.plugin}>
          <contributed.Renderer
            summary={confirmation.summary}
            schema={confirmation.summarySchema}
          />
        </PluginBoundary>
      ) : (
        <WriteSummary summary={confirmation.summary} schema={confirmation.summarySchema} />
      )}

      {answerable ? (
        <>
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.approve}
              disabled={state === 'answering'}
              onClick={() => onAnswer(true)}
            >
              Approve
            </button>
            <button
              type="button"
              className={styles.reject}
              disabled={state === 'answering'}
              onClick={() => onAnswer(false)}
            >
              Reject
            </button>
          </div>
          {confirmation.expiresAt && (
            <p className={styles.expiry}>Answerable until {when(confirmation.expiresAt)}.</p>
          )}
        </>
      ) : (
        <p className={styles.outcome} role="status">
          {outcomeText(state)}
        </p>
      )}
    </div>
  );
}

function outcomeText(state: ConfirmationState): string {
  switch (state) {
    case 'applied':
      return 'Applied.';
    case 'declined':
      return 'Declined. Nothing was changed.';
    case 'expired':
      return 'This proposal is too old to apply. Ask for it again.';
    default:
      return 'This proposal is no longer waiting for an answer.';
  }
}

const when = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
