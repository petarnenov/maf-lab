import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import type { AgentModel, AgentModels } from '../api/types';
import { useApi } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { usd } from './format';
import { coverageKeys } from './keys';
import styles from './CoveragePage.module.css';

/** Exactly one model from the server's allowlist; one the account cannot use cannot be picked. */
export function ModelPicker({
  path,
  selected,
  onSelect,
  onDefault,
}: {
  path: string;
  selected: string | null;
  onSelect: (tag: string) => void;
  /** Told the default model once the list arrives, if it is available, so it can be preselected. */
  onDefault?: (tag: string) => void;
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
  return (
    <div>
      <fieldset className={styles.models}>
        <legend>Model</legend>
        {models.data.models.map((m) => (
          <ModelOption key={m.tag} model={m} checked={m.tag === selected} onSelect={onSelect} />
        ))}
      </fieldset>
      <p className={styles.estimate}>
        {chosen?.estimate ? (
          <>
            Estimated cost for up to {models.data.maxAttempts} attempts: {usd(chosen.estimate.costUsd)}
            {chosen.priceIsEstimate && ' (prices are estimates)'}.{' '}
          </>
        ) : null}
        The run stops at {usd(models.data.maxCostUsd)} or {models.data.maxTokens.toLocaleString()} tokens,
        whichever comes first.
      </p>
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
