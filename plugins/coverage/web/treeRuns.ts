import type { CoverageTreeFile, RunSummary } from './types';
import { instant } from './format';

/** How many rows follow their run live; the rest show the tree's snapshot, refetched while any run is active. */
export const MAX_LIVE_ROWS = 3;

/** Outcomes a row keeps after its run, until the file is measured again. A candidate shows its own badge. */
export const KEPT_OUTCOMES = new Set(['completed_no_change', 'failed', 'canceled', 'verification_failed']);

/** Whether a file's run is being worked on by the agent (a candidate waits for a person). */
export const agentHas = (run: RunSummary | null | undefined): boolean =>
  !!run && run.active && run.state !== 'candidate';

/** The runs whose rows follow them live: the most recently updated active ones, at most {@link MAX_LIVE_ROWS}. */
export function liveRunIds(files: CoverageTreeFile[]): ReadonlySet<string> {
  return new Set(
    files
      .map((f) => f.run)
      .filter((r): r is RunSummary => agentHas(r))
      .sort((a, b) => instant(b.updatedAt) - instant(a.updatedAt))
      .slice(0, MAX_LIVE_ROWS)
      .map((r) => r.id),
  );
}
