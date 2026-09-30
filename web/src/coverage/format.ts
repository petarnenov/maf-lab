import type { RunState } from '../api/types';

export const pct = (value: number) => `${value.toFixed(1)}%`;

export const shortSha = (sha: string) => sha.slice(0, 7);

export const when = (iso: string) => new Date(iso).toLocaleString();

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
