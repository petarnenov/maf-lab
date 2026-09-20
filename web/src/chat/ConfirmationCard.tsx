import type { ConfirmationRequiredData } from '../api/types';
import type { ConfirmationState } from './chatReducer';
import styles from './ConfirmationCard.module.css';

interface Props {
  confirmation: ConfirmationRequiredData;
  state: ConfirmationState;
  onAnswer: (approve: boolean) => void;
}

/**
 * A write waiting for the advisor, in the conversation where it was proposed rather than over it.
 * It shows what the server would do — the account, the fee now, the change, the fee after — and offers the two
 * answers. Once it has been answered, or is too late to answer, it says so instead of asking again.
 */
export function ConfirmationCard({ confirmation, state, onAnswer }: Props) {
  const a = confirmation.adjustment;
  const answerable = state === 'waiting' || state === 'answering';

  return (
    <div
      className={`${styles.card} ${styles[state]}`}
      data-testid="confirmation-card"
      data-state={state}
    >
      <p className={styles.question}>{confirmation.question}</p>

      <dl className={styles.facts}>
        <Fact label="Account">
          {a.accountId} — {a.accountName}
        </Fact>
        <Fact label="Fee now">{money(a.currentFee, a.currency)}</Fact>
        <Fact label="Adjustment">{money(a.amount, a.currency)}</Fact>
        <Fact label="Fee after">{money(a.resultingFee, a.currency)}</Fact>
        <Fact label="Period">
          {a.periodStart} to {a.periodEnd}
        </Fact>
      </dl>

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

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className={styles.fact}>
      <dt>{label}</dt>
      <dd>{children}</dd>
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
      return 'This proposal is too old to apply. Ask for the adjustment again.';
    default:
      return 'This proposal is no longer waiting for an answer.';
  }
}

const money = (value: number, currency: string) =>
  `${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;

const when = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
