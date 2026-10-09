import type { RunState } from './types';

export const pct = (value: number) => `${value.toFixed(1)}%`;

export const usd = (value: number) => `$${value.toFixed(value < 1 ? 3 : 2)}`;

/** A duration in minutes as hours and minutes: "2 h", "1 h 30 min", "45 min". */
export function duration(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return [h > 0 ? `${h} h` : '', m > 0 || h === 0 ? `${m} min` : ''].filter(Boolean).join(' ');
}

/** A run's duration in milliseconds, compact: "42s", "3m 05s", "1h 02m"; "—" when there is none. */
export function elapsed(ms: number | null | undefined): string {
  if (ms == null || !Number.isFinite(ms)) return '—';
  const seconds = Math.max(0, Math.floor(ms / 1000));
  const two = (n: number) => String(n).padStart(2, '0');
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${two(seconds % 60)}s`;
  return `${Math.floor(seconds / 3600)}h ${two(Math.floor(seconds / 60) % 60)}m`;
}

/**
 * What a run cost, in US dollars: "$0.042" under a dollar, "$1.27" from one, "<$0.001" for an amount three decimals
 * would show as nothing, "$0.00" for nothing spent, "—" when unknown. An amount priced at estimated rates reads "≈$0.042".
 */
export function cost(value: number | null | undefined, estimate = false): string {
  if (value == null || !Number.isFinite(value)) return '—';
  if (value <= 0) return '$0.00';
  const approx = estimate ? '≈' : '';
  if (value.toFixed(3) === '0.000') return `${approx}<$0.001`;
  const amount =
    Number(value.toFixed(3)) < 1
      ? value.toFixed(3)
      : value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return `${approx}$${amount}`;
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
