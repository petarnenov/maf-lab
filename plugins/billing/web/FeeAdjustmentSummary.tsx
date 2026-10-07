import type { ReactNode } from 'react';
import styles from './FeeAdjustmentSummary.module.css';

/** A fee adjustment waiting for the advisor, as billing's tool summarizes it. */
interface FeeAdjustment {
  accountId: string;
  accountName: string;
  currentFee: number;
  amount: number;
  resultingFee: number;
  currency: string;
  periodStart: string;
  periodEnd: string;
}

/**
 * What a fee adjustment would do, as the advisor checks it before it applies: the account, the fee now, the change, the
 * fee after and the period, with money in the account's currency. Billing's renderer for `propose_fee_adjustment`
 * (generalize-write-confirmation); the card around it keeps the question and the two answers.
 */
export function FeeAdjustmentSummary({ summary }: { summary: unknown; schema: unknown }) {
  const a = summary as FeeAdjustment;
  return (
    <dl className={styles.facts} data-testid="fee-adjustment-summary">
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
  );
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className={styles.fact}>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

const money = (value: number, currency: string) =>
  `${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;
