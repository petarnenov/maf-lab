import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { CoverageFileDetail } from './types';
import { useApi } from '@maf/plugin-api';
import { coverageKeys } from './keys';
import { RaiseThresholdDialog } from './RaiseThresholdDialog';
import { useRunActivity } from './useRunActivity';
import { RunStatus } from './RunStatus';
import { useRunEvents } from './useRunEvents';
import styles from './CoveragePage.module.css';

/**
 * A file's threshold, for an admin. A threshold above the file's coverage means "reach it": saving one — raised, kept
 * or lowered, or the default when clearing an override — opens the confirmation to start a run. One that does not
 * raise the stored threshold can also be saved without a run; a raise above coverage needs one (the server says so).
 * A threshold coverage already meets saves at once. While a run is active the control is locked.
 */
export function ThresholdControl({ detail, defaultThreshold }: { detail: CoverageFileDetail; defaultThreshold: number }) {
  const api = useApi();
  const client = useQueryClient();
  const { summary, path } = detail;
  const run = useRunEvents(detail.run);
  const [value, setValue] = useState(String(summary.threshold));
  const [raising, setRaising] = useState<{ target: number; useDefault: boolean } | null>(null);
  const activity = useRunActivity(run, true);

  const save = useMutation({
    mutationFn: (pct: number | null) =>
      api(`/api/coverage/thresholds?path=${encodeURIComponent(path)}`, { method: 'PUT', body: { pct } }),
    onSuccess: () => void client.invalidateQueries({ queryKey: coverageKeys.all }),
  });

  const parsed = Number(value);
  const valid = value.trim() !== '' && Number.isInteger(parsed) && parsed >= 0 && parsed <= 100;

  const submit = () => {
    if (!valid) return;
    if (parsed > summary.pct) setRaising({ target: parsed, useDefault: false });
    else save.mutate(parsed);
  };

  const useDefault = () => {
    if (defaultThreshold > summary.pct) setRaising({ target: defaultThreshold, useDefault: true });
    else save.mutate(null);
  };

  const control = run?.active ? (
    <div className={styles.thresholdControl} aria-label="Threshold">
      <span>Threshold {summary.threshold}% — locked while a run is active.</span>
      <RunStatus run={run} />
      {activity.button}
    </div>
  ) : (
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
        <button type="button" onClick={useDefault} disabled={save.isPending}>
          Use default
        </button>
      )}
      {activity.button}
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
          targetPct={raising.target}
          useDefault={raising.useDefault}
          canSaveWithoutRun={raising.useDefault || raising.target <= summary.threshold}
          onClose={() => setRaising(null)}
        />
      )}
    </div>
  );

  // The Activity dialog has one place outside both layouts, so the lock lifting at the run's end does not remount it.
  return (
    <>
      {control}
      {activity.dialog}
    </>
  );
}
