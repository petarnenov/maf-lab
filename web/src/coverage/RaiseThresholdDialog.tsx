import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiError } from '../api/client';
import type { RunSummary } from '../api/types';
import { useApi } from '../auth/useAuth';
import { coverageKeys } from './keys';
import { ModelPicker } from './ModelPicker';
import { pct } from './format';
import styles from './CoveragePage.module.css';

/**
 * Raising a threshold above the file's coverage: confirm, then pick a model, then start. The threshold is saved by
 * the server only when the run is accepted, so cancelling at any step — or a start that fails — changes nothing.
 */
export function RaiseThresholdDialog({
  path,
  currentPct,
  targetPct,
  onClose,
}: {
  path: string;
  currentPct: number;
  targetPct: number;
  onClose: () => void;
}) {
  const api = useApi();
  const client = useQueryClient();
  const [step, setStep] = useState<'confirm' | 'model'>('confirm');
  const [model, setModel] = useState<string | null>(null);

  const start = useMutation({
    mutationFn: () =>
      api<RunSummary>('/api/coverage/runs', { method: 'POST', body: { path, pct: targetPct, model } }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: coverageKeys.all });
      onClose();
    },
  });

  return (
    <div className={styles.backdrop}>
      <div role="dialog" aria-modal="true" aria-labelledby="raise-title" className={styles.dialog}>
        <h3 id="raise-title" className={styles.dialogTitle}>
          Raise the threshold to {targetPct}%?
        </h3>
        {step === 'confirm' ? (
          <>
            <p>
              This file is at <strong>{pct(currentPct)}</strong>. Raising its threshold to{' '}
              <strong>{targetPct}%</strong> starts an agent run that writes tests for it, in up to
              five attempts.
            </p>
            <div className={styles.dialogActions}>
              <button type="button" onClick={onClose}>
                Cancel
              </button>
              <button type="button" className={styles.primary} onClick={() => setStep('model')}>
                Continue
              </button>
            </div>
          </>
        ) : (
          <>
            <ModelPicker
              path={path}
              selected={model}
              onSelect={setModel}
              onDefault={(tag) => setModel((m) => m ?? tag)}
            />
            {start.isError && (
              <p className={styles.errorText} role="alert">
                {startError(start.error)}
              </p>
            )}
            <div className={styles.dialogActions}>
              <button type="button" onClick={onClose} disabled={start.isPending}>
                Cancel
              </button>
              <button
                type="button"
                className={styles.primary}
                disabled={!model || start.isPending}
                onClick={() => start.mutate()}
              >
                {start.isPending ? 'Starting…' : 'Start run'}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

/** What went wrong starting a run, in words for the person who pressed Start. */
function startError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 503) return 'The test agent is unavailable. The threshold was not changed.';
    if (error.status === 409) return 'A run is already active for this file.';
    if (error.status === 400 || error.status === 422) return 'That model cannot be used. Pick another.';
  }
  return 'Could not start the run. The threshold was not changed.';
}
