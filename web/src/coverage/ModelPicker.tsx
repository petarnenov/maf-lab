import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import type { AgentModel, AgentModels } from '../api/types';
import { useApi } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { type BudgetInput, describeBudget, parseBudget } from './budget';
import { usd } from './format';
import { coverageKeys } from './keys';
import styles from './CoveragePage.module.css';

/**
 * Exactly one model from the server's allowlist; one the account cannot use cannot be picked. Beside it, the run's
 * optional budget: both caps start empty, which is unlimited, and then only the attempt cap stops the run.
 */
export function ModelPicker({
  path,
  selected,
  onSelect,
  onDefault,
  budget,
  onBudget,
}: {
  path: string;
  selected: string | null;
  onSelect: (tag: string) => void;
  /** Told the default model once the list arrives, if it is available, so it can be preselected. */
  onDefault?: (tag: string) => void;
  budget: BudgetInput;
  onBudget: (budget: BudgetInput) => void;
}) {
  const api = useApi();
  const models = useQuery({
    queryKey: coverageKeys.models(path),
    queryFn: () => api<AgentModels>(`/api/coverage/models?path=${encodeURIComponent(path)}`),
  });

  const preselect = models.data?.models.find((m) => m.isDefault && m.available)?.tag;
  useEffect(() => {
    if (preselect) onDefault?.(preselect);
  }, [preselect, onDefault]);

  if (models.isLoading) return <p className={page.muted}>Checking which models are available…</p>;
  if (models.isError || !models.data)
    return (
      <p className={page.error} role="alert">
        Could not load the models.
      </p>
    );

  const chosen = models.data.models.find((m) => m.tag === selected);
  const { budget: caps, errors } = parseBudget(budget);
  const overBudget =
    chosen?.estimate != null && caps?.maxCostUsd != null && chosen.estimate.costUsd > caps.maxCostUsd;
  return (
    <div>
      <fieldset className={styles.models}>
        <legend>Model</legend>
        {models.data.models.map((m) => (
          <ModelOption key={m.tag} model={m} checked={m.tag === selected} onSelect={onSelect} />
        ))}
      </fieldset>
      <fieldset className={styles.budget}>
        <legend>Budget (optional)</legend>
        <BudgetField
          label="Max tokens"
          value={budget.maxTokens}
          error={errors.maxTokens}
          inputMode="numeric"
          onChange={(maxTokens) => onBudget({ ...budget, maxTokens })}
        />
        <BudgetField
          label="Max cost (USD)"
          value={budget.maxCostUsd}
          error={errors.maxCostUsd}
          inputMode="decimal"
          onChange={(maxCostUsd) => onBudget({ ...budget, maxCostUsd })}
        />
      </fieldset>
      <p className={styles.estimate}>
        {chosen?.estimate ? (
          <>
            Estimated cost for up to {models.data.maxAttempts} attempts: {usd(chosen.estimate.costUsd)}
            {chosen.priceIsEstimate && ' (prices are estimates)'}.{' '}
          </>
        ) : null}
        {caps == null
          ? `Budget: unlimited. The run stops after at most ${models.data.maxAttempts} attempts.`
          : `The run stops at ${describeBudget(caps)}, whichever comes first, or after ${models.data.maxAttempts} attempts.`}
      </p>
      {overBudget && (
        <p className={styles.errorText} role="status">
          The estimate is above this budget, so the run may stop before it reaches the target.
        </p>
      )}
    </div>
  );
}

function BudgetField({
  label,
  value,
  error,
  inputMode,
  onChange,
}: {
  label: string;
  value: string;
  error?: string;
  inputMode: 'numeric' | 'decimal';
  onChange: (value: string) => void;
}) {
  const id = `budget-${label.replace(/\W+/g, '-').toLowerCase()}`;
  return (
    <label className={styles.budgetField} htmlFor={id}>
      <span>{label}</span>
      <input
        id={id}
        type="text"
        inputMode={inputMode}
        placeholder="unlimited"
        value={value}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      {error && (
        <span id={`${id}-error`} className={styles.errorText}>
          {error}
        </span>
      )}
    </label>
  );
}

function ModelOption({
  model,
  checked,
  onSelect,
}: {
  model: AgentModel;
  checked: boolean;
  onSelect: (tag: string) => void;
}) {
  return (
    <label className={`${styles.model} ${model.available ? '' : styles.unavailable}`}>
      <input
        type="radio"
        name="agent-model"
        value={model.tag}
        checked={checked}
        disabled={!model.available}
        onChange={() => onSelect(model.tag)}
      />
      <span>
        <strong>{model.displayName}</strong>
        {model.isDefault && <span className={styles.recommended}> recommended</span>}
        <span className={styles.modelPrice}>
          {' '}
          ${model.inputPerMTok} in / ${model.outputPerMTok} out per 1M tokens
          {model.priceIsEstimate ? ' (estimate)' : ''}
        </span>
        <span className={styles.modelNote}>
          {model.available ? model.bestFor : (model.unavailableReason ?? 'Unavailable.')}
        </span>
      </span>
    </label>
  );
}
