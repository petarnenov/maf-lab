import { useEffect } from 'react';
import type { AgentModel } from '../api/types';
import page from '../components/Page.module.css';
import { type BudgetInput, describeBudget, parseBudget } from './budget';
import { duration, usd } from './format';
import { estimateRun, type LimitKey, type LimitsInput, defaultLimits, parseLimits } from './limits';
import styles from './CoveragePage.module.css';
import { useAgentModels } from './useAgentModels';

const LIMIT_LABELS: Record<LimitKey, { label: string; hint: string }> = {
  maxAttempts: { label: 'Max attempts', hint: 'Attempts to reach the target.' },
  toolRoundsPerAttempt: { label: 'Tool rounds per attempt', hint: 'Model calls, each with the tools it asks for.' },
  testRunsPerAttempt: { label: 'Test runs per attempt', hint: 'Runs the model may start itself; each attempt is measured anyway.' },
  deadlineMinutes: { label: 'Run deadline (minutes)', hint: 'The run is canceled when it has taken this long.' },
  maxSuspectedBugs: { label: 'Suspected bugs per run', hint: 'Failing tests the agent may report as bugs; 0 reports none.' },
};

/**
 * A cost cap to start from: the estimate rounded up to the cent (or to two significant digits below a cent), so the cap
 * it fills never sits below the estimate it came from.
 */
function costField(value: number): string {
  if (value >= 0.01) return String(Math.ceil(Math.round(value * 1_000_000) / 10_000) / 100);
  const scale = 10 ** (1 - Math.floor(Math.log10(value)));
  return String(Math.ceil(value * scale) / scale);
}

/**
 * Exactly one model from the server's allowlist; one the account cannot use cannot be picked. Beside it, every limit
 * the run has, filled with its default: the attempts, tool rounds and test runs it may lower, the fixed ones, and the
 * two budget caps, unlimited unless set. The estimate prices the limits entered at the selected model's rates.
 */
export function ModelPicker({
  path,
  targetPct,
  selected,
  onSelect,
  onDefault,
  limits,
  onLimits,
  budget,
  onBudget,
}: {
  path: string;
  targetPct: number;
  selected: string | null;
  onSelect: (tag: string) => void;
  /** Told the default model once the list arrives, if it is available, so it can be preselected. */
  onDefault?: (tag: string) => void;
  /** The limits as typed; null until the server's defaults arrive, when onLimits is told them. */
  limits: LimitsInput | null;
  onLimits: (limits: LimitsInput) => void;
  budget: BudgetInput;
  onBudget: (budget: BudgetInput) => void;
}) {
  const models = useAgentModels(path);

  const preselect = models.data?.models.find((m) => m.isDefault && m.available)?.tag;
  useEffect(() => {
    if (preselect) onDefault?.(preselect);
  }, [preselect, onDefault]);

  const bounds = models.data?.limits;
  useEffect(() => {
    if (bounds && limits === null) onLimits(defaultLimits(bounds));
  }, [bounds, limits, onLimits]);

  if (models.isLoading) return <p className={page.muted}>Checking which models are available…</p>;
  if (models.isError || !models.data)
    return (
      <p className={page.error} role="alert">
        Could not load the models.
      </p>
    );

  const { limits: bound } = models.data;
  const input = limits ?? defaultLimits(bound);
  const { limits: chosenLimits, errors: limitErrors } = parseLimits(input, bound);
  const chosen = models.data.models.find((m) => m.tag === selected);
  const estimate =
    chosen && models.data.estimate && chosenLimits
      ? estimateRun(models.data.estimate, chosen, chosenLimits.maxAttempts, chosenLimits.toolRoundsPerAttempt)
      : null;
  const attempts = chosenLimits?.maxAttempts ?? bound.maxAttempts.default;
  const { budget: caps, errors } = parseBudget(budget);
  const overBudget =
    estimate != null &&
    ((caps?.maxCostUsd != null && estimate.costUsd > caps.maxCostUsd) ||
      (caps?.maxTokens != null && estimate.tokens > caps.maxTokens));

  const setUnlimited = (cap: 'tokens' | 'cost', unlimited: boolean) => {
    if (cap === 'tokens')
      onBudget({
        ...budget,
        tokensUnlimited: unlimited,
        maxTokens: !unlimited && budget.maxTokens === '' && estimate ? String(estimate.tokens) : budget.maxTokens,
      });
    else
      onBudget({
        ...budget,
        costUnlimited: unlimited,
        maxCostUsd: !unlimited && budget.maxCostUsd === '' && estimate ? costField(estimate.costUsd) : budget.maxCostUsd,
      });
  };

  return (
    <div>
      <fieldset className={styles.models}>
        <legend>Model</legend>
        {models.data.models.map((m) => (
          <ModelOption key={m.tag} model={m} checked={m.tag === selected} onSelect={onSelect} />
        ))}
      </fieldset>
      <fieldset className={styles.budget}>
        <legend>Limits</legend>
        {(Object.keys(LIMIT_LABELS) as LimitKey[]).map((key) => (
          <NumberField
            key={key}
            id={`limit-${key}`}
            label={LIMIT_LABELS[key].label}
            hint={`${LIMIT_LABELS[key].hint} From ${bound[key].min} to ${bound[key].max}; default ${bound[key].default}${
              key === 'deadlineMinutes' ? ` (${duration(bound[key].default)})` : ''
            }.`}
            value={input[key]}
            error={limitErrors[key]}
            inputMode="numeric"
            onChange={(value) => onLimits({ ...input, [key]: value })}
          />
        ))}
        <FixedLimits targetPct={targetPct} />
      </fieldset>
      <fieldset className={styles.budget}>
        <legend>Budget</legend>
        <NumberField
          id="budget-max-tokens"
          label="Max tokens"
          value={budget.maxTokens}
          error={errors.maxTokens}
          inputMode="numeric"
          unlimited={budget.tokensUnlimited}
          onUnlimited={(u) => setUnlimited('tokens', u)}
          onChange={(maxTokens) => onBudget({ ...budget, maxTokens })}
        />
        <NumberField
          id="budget-max-cost-usd"
          label="Max cost (USD)"
          value={budget.maxCostUsd}
          error={errors.maxCostUsd}
          inputMode="decimal"
          unlimited={budget.costUnlimited}
          onUnlimited={(u) => setUnlimited('cost', u)}
          onChange={(maxCostUsd) => onBudget({ ...budget, maxCostUsd })}
        />
      </fieldset>
      <p className={styles.estimate}>
        {estimate && chosen ? (
          <>
            Estimated cost: about {usd(estimate.perAttemptUsd)} per attempt, up to {usd(estimate.costUsd)} for{' '}
            {attempts} attempt{attempts === 1 ? '' : 's'} ({estimate.tokens.toLocaleString('en-US')} tokens)
            {chosen.priceIsEstimate && '; prices are estimates'}.{' '}
          </>
        ) : null}
        {caps == null
          ? `Budget: unlimited. The run stops after at most ${attempts} attempt${attempts === 1 ? '' : 's'}.`
          : `The run stops at ${describeBudget(caps)}, whichever comes first, or after ${attempts} attempt${attempts === 1 ? '' : 's'}.`}
      </p>
      {overBudget && (
        <p className={styles.errorText} role="status">
          The estimate is above this budget, so the run may stop before it reaches the target.
        </p>
      )}
    </div>
  );
}

/** The one limit set elsewhere: the target is the threshold this dialog raises. */
function FixedLimits({ targetPct }: { targetPct: number }) {
  return (
    <dl className={styles.fixedLimits}>
      <div>
        <dt>Target line coverage</dt>
        <dd>{targetPct}%</dd>
      </div>
    </dl>
  );
}

function NumberField({
  id,
  label,
  hint,
  value,
  error,
  inputMode,
  unlimited,
  onUnlimited,
  onChange,
}: {
  id: string;
  label: string;
  hint?: string;
  value: string;
  error?: string;
  inputMode: 'numeric' | 'decimal';
  /** Present for a cap: whether it is unlimited, its default. */
  unlimited?: boolean;
  onUnlimited?: (unlimited: boolean) => void;
  onChange: (value: string) => void;
}) {
  const described = [hint ? `${id}-hint` : null, error && !unlimited ? `${id}-error` : null].filter(Boolean).join(' ');
  return (
    <div className={styles.budgetField}>
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type="text"
        inputMode={inputMode}
        placeholder={unlimited ? 'unlimited' : undefined}
        value={unlimited ? '' : value}
        disabled={unlimited}
        aria-invalid={error && !unlimited ? true : undefined}
        aria-describedby={described || undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      {onUnlimited && (
        <label className={styles.unlimitedToggle}>
          <input type="checkbox" checked={unlimited} onChange={(e) => onUnlimited(e.target.checked)} /> Unlimited
        </label>
      )}
      {hint && (
        <span id={`${id}-hint`} className={styles.modelNote}>
          {hint}
        </span>
      )}
      {error && !unlimited && (
        <span id={`${id}-error`} className={styles.errorText}>
          {error}
        </span>
      )}
    </div>
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
