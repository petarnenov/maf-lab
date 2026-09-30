import type { RunState } from '../api/types';

export const pct = (value: number) => `${value.toFixed(1)}%`;

export const usd = (value: number) => `$${value.toFixed(value < 1 ? 3 : 2)}`;

/** A duration in minutes as hours and minutes: "2 h", "1 h 30 min", "45 min". */
export function duration(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return [h > 0 ? `${h} h` : '', m > 0 || h === 0 ? `${m} min` : ''].filter(Boolean).join(' ');
}

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

/** Why the agent stopped, in a word or two, beside a run's state ("No change · budget"). */
export const STOP_LABELS: Record<string, string> = {
  target: 'target reached',
  attempts: 'all attempts used',
  budget: 'budget',
};

/** A run's reason in user-facing words: a stop reason by its label, any other reason as the server said it. */
export const reasonLabel = (reason: string | null | undefined): string | null =>
  reason ? (STOP_LABELS[reason] ?? reason) : null;

/** The agent's stop as the timeline's closing line. */
export function stopSentence(stop: { reason: string; lastAttempt: number; notStarted?: number | null }): string {
  switch (stop.reason) {
    case 'target':
      return `Reached the target in attempt ${stop.lastAttempt}.`;
    case 'attempts':
      return `Used all ${stop.lastAttempt} attempts without reaching the target.`;
    case 'budget':
      return stop.notStarted != null
        ? `Stopped before attempt ${stop.notStarted}: the budget would be exceeded.`
        : `Stopped in attempt ${stop.lastAttempt}: the budget was spent.`;
    default:
      return `Stopped (${stop.reason}).`;
  }
}
