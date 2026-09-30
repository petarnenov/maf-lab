import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiError } from '../api/client';
import type { RunSummary } from '../api/types';
import { useApi } from '../auth/useAuth';
import { coverageKeys } from './keys';
import { emptyBudget, parseBudget } from './budget';
import { ModelPicker } from './ModelPicker';
import { pct } from './format';
import styles from './CoveragePage.module.css';

/**
 * Saving a threshold above the file's coverage: confirm, then pick a model, then start. The threshold is saved by
 * the server only when the run is accepted, so cancelling at any step — or a start that fails — changes nothing.
 * The confirmation can also save the threshold without a run.
 */
export function RaiseThresholdDialog({
  path,
  currentPct,
  targetPct,
  useDefault = false,
  onClose,
}: {
  path: string;
  currentPct: number;
  targetPct: number;
  /** The target is the default: saving without a run clears the override rather than storing one. */
  useDefault?: boolean;
  onClose: () => void;
}) {
  const api = useApi();
  const client = useQueryClient();
  const [step, setStep] = useState<'confirm' | 'model'>('confirm');
  const [model, setModel] = useState<string | null>(null);
  const [budgetInput, setBudgetInput] = useState(emptyBudget);
  const { budget, errors } = parseBudget(budgetInput);
  const budgetValid = !errors.maxTokens && !errors.maxCostUsd;

  const saveOnly = useMutation({
    mutationFn: () =>
      api(`/api/coverage/thresholds?path=${encodeURIComponent(path)}`, {
        method: 'PUT',
        body: { pct: useDefault ? null : targetPct },
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: coverageKeys.all });
      onClose();
    },
  });

  const start = useMutation({
    mutationFn: () =>
      api<RunSummary>('/api/coverage/runs', { method: 'POST', body: { path, pct: targetPct, model, budget } }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: coverageKeys.all });
      onClose();
    },
  });

  return (
    <div className={styles.backdrop}>
      <div role="dialog" aria-modal="true" aria-labelledby="raise-title" className={styles.dialog}>
        <h3 id="raise-title" className={styles.dialogTitle}>
          Reach {targetPct}%?
        </h3>
        {step === 'confirm' ? (
          <>
            <p>
              This file is at <strong>{pct(currentPct)}</strong>. A threshold of <strong>{targetPct}%</strong>{' '}
              {useDefault ? '(the default) ' : ''}starts an agent run that writes tests for it, in up to five attempts.
            </p>
            {saveOnly.isError && (
              <p className={styles.errorText} role="alert">
                Could not save the threshold.
              </p>
            )}
            <div className={styles.dialogActions}>
              <button type="button" onClick={onClose} disabled={saveOnly.isPending}>
                Cancel
              </button>
              <button type="button" onClick={() => saveOnly.mutate()} disabled={saveOnly.isPending}>
                Save without a run
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
              budget={budgetInput}
              onBudget={setBudgetInput}
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
                disabled={!model || !budgetValid || start.isPending}
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
    if (error.status === 400) return 'The server did not accept these settings. Check the budget and try again.';
    if (error.status === 422) return 'That model cannot be used. Pick another.';
  }
  return 'Could not start the run. The threshold was not changed.';
}
