import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { CoverageFileDetail } from '../api/types';
import { useApi } from '../auth/useAuth';
import { coverageKeys } from './keys';
import { RaiseThresholdDialog } from './RaiseThresholdDialog';
import { RunStatus } from './RunStatus';
import { useRunEvents } from './useRunEvents';
import styles from './CoveragePage.module.css';

/**
 * A file's threshold, for an admin. Lowering, keeping, clearing, or raising to what coverage already meets saves at
 * once; raising above coverage opens the confirmation. While a run is active the control is locked and shows it.
 */
export function ThresholdControl({ detail }: { detail: CoverageFileDetail }) {
  const api = useApi();
  const client = useQueryClient();
  const { summary, path } = detail;
  const run = useRunEvents(detail.run);
  const [value, setValue] = useState(String(summary.threshold));
  const [raising, setRaising] = useState<number | null>(null);

  const save = useMutation({
    mutationFn: (pct: number | null) =>
      api(`/api/coverage/thresholds?path=${encodeURIComponent(path)}`, { method: 'PUT', body: { pct } }),
    onSuccess: () => void client.invalidateQueries({ queryKey: coverageKeys.all }),
  });

  if (run?.active) {
    return (
      <div className={styles.thresholdControl} aria-label="Threshold">
        <span>
          Threshold {summary.threshold}% — locked while a run is active.
        </span>
        <RunStatus run={run} />
      </div>
    );
  }

  const parsed = Number(value);
  const valid = value.trim() !== '' && Number.isInteger(parsed) && parsed >= 0 && parsed <= 100;

  const submit = () => {
    if (!valid) return;
    if (parsed > summary.threshold && parsed > summary.pct) setRaising(parsed);
    else save.mutate(parsed);
  };

  return (
    <div className={styles.thresholdControl}>
      <label>
        Threshold{' '}
        <input
          type="number"
          min={0}
          max={100}
          step={1}
          aria-label="Threshold (%)"
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
        %
      </label>
      <button type="button" onClick={submit} disabled={!valid || save.isPending}>
        Save
      </button>
      {summary.thresholdIsOverride && (
        <button type="button" onClick={() => save.mutate(null)} disabled={save.isPending}>
          Use default
        </button>
      )}
      {!valid && <span className={styles.errorText}>Enter a whole number from 0 to 100.</span>}
      {save.isError && (
        <span className={styles.errorText} role="alert">
          Could not save the threshold.
        </span>
      )}
      {run && <RunStatus run={run} />}
      {raising !== null && (
        <RaiseThresholdDialog
          path={path}
          currentPct={summary.pct}
          targetPct={raising}
          onClose={() => setRaising(null)}
        />
      )}
    </div>
  );
}
