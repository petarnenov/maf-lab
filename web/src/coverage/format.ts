import type { RunState } from '../api/types';

export const pct = (value: number) => `${value.toFixed(1)}%`;

export const usd = (value: number) => `$${value.toFixed(value < 1 ? 3 : 2)}`;

export const shortSha = (sha: string) => sha.slice(0, 7);

/**
 * An instant from the api. Its times are UTC; one written without a zone (a stored DateTime) is read as UTC too, not
 * as the browser's local time.
 */
export const instant = (iso: string) => Date.parse(/(Z|[+-]\d\d:?\d\d)$/.test(iso) ? iso : `${iso}Z`);

export const when = (iso: string) => new Date(instant(iso)).toLocaleString();

/** What each run state reads as on screen. */
export const RUN_LABELS: Record<RunState, string> = {
  submitted: 'Submitted',
  working: 'Working',
  verifying: 'Verifying',
  candidate: 'Candidate ready',
  accepted: 'Accepted',
  discarded: 'Discarded',
  completed_no_change: 'No change',
  failed: 'Failed',
  canceled: 'Canceled',
  verification_failed: 'Verification failed',
};
