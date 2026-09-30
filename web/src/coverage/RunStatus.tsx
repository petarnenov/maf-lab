import type { RunSummary } from '../api/types';
import { pct, reasonLabel, RUN_LABELS } from './format';
import styles from './CoveragePage.module.css';

/** Where a run stands: its state, the attempt, the latest coverage and what it has cost so far. */
export function RunStatus({ run }: { run: RunSummary }) {
  return (
    <p className={styles.runStatus} role="status" aria-label="Run status">
      <strong>{RUN_LABELS[run.state]}</strong>
      {run.attempt > 0 && (
        <span>
          {' '}
          · attempt {run.attempt}/{run.maxAttempts}
        </span>
      )}
      {run.active && run.phase && <span> · {run.phase}</span>}
      {run.lastPct != null && <span> · {pct(run.lastPct)}</span>}
      <span> · target {run.targetPct}%</span>
      {run.costUsd > 0 && <span> · ${run.costUsd.toFixed(3)}</span>}
      {run.reason && <span> · {reasonLabel(run.reason)}</span>}
    </p>
  );
}
