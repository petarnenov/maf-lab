import type { CoverageTreeFile, RunSummary } from './types';
import { instant, reasonLabel, RUN_LABELS } from './format';
import { agentHas, KEPT_OUTCOMES } from './treeRuns';
import { LiveMarker } from './LiveMarker';
import { useRunEvents } from './useRunEvents';
import styles from './CoveragePage.module.css';

/** A file row's run: live while the agent works, the tree's snapshot otherwise, and the last outcome after it. */
export function TreeRunBadge({ file, live }: { file: CoverageTreeFile; live: boolean }) {
  const run = file.run;
  if (run && agentHas(run)) return live ? <LiveBadge run={run} /> : <StaticBadge run={run} />;
  if (run && KEPT_OUTCOMES.has(run.state) && instant(run.updatedAt) > instant(file.measuredAt)) {
    const why = reasonLabel(run.reason);
    return (
      <span className={styles.lastOutcome} title={`Last run: ${RUN_LABELS[run.state]}${why ? ` (${why})` : ''}`}>
        {RUN_LABELS[run.state].toLowerCase()}
      </span>
    );
  }
  return null;
}

function StaticBadge({ run }: { run: RunSummary }) {
  return (
    <span className={styles.runBadge}>
      {RUN_LABELS[run.state]}
      {run.state === 'working' && ` ${run.attempt}/${run.maxAttempts}`}
    </span>
  );
}

function LiveBadge({ run }: { run: RunSummary }) {
  const live = useRunEvents(run) ?? run;
  return (
    <span className={styles.runBadge} aria-label={`Run ${RUN_LABELS[live.state]}`}>
      <LiveMarker /> {RUN_LABELS[live.state]}
      {live.state === 'working' && (live.attempt === 0 ? ' baseline' : ` ${live.attempt}/${live.maxAttempts}`)}
      {live.phase && ` · ${live.phase}`}
    </span>
  );
}
