import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { AgentModels, RunSummary } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { CoveragePage } from './CoveragePage';
import { detail, stubCoverageApi } from './CoveragePage.test';
import { sampleTree } from './treeModel.test';

const models: AgentModels = {
  maxAttempts: 5,
  maxTokens: 400000,
  maxCostUsd: 2,
  models: [
    {
      tag: 'glm-5.3:cloud',
      displayName: 'GLM 5.3',
      inputPerMTok: 0.6,
      outputPerMTok: 2.2,
      bestFor: 'Recommended.',
      isDefault: true,
      priceIsEstimate: true,
      available: true,
      unavailableReason: null,
      estimate: { inputTokens: 80000, outputTokens: 20000, costUsd: 0.092 },
    },
    {
      tag: 'kimi-k3:cloud',
      displayName: 'Kimi K3',
      inputPerMTok: 0.6,
      outputPerMTok: 2.5,
      bestFor: 'Long files.',
      isDefault: false,
      priceIsEstimate: true,
      available: false,
      unavailableReason: 'Not available to this account.',
      estimate: { inputTokens: 80000, outputTokens: 20000, costUsd: 0.098 },
    },
    {
      tag: 'glm-5.3-flash:cloud',
      displayName: 'GLM 5.3 Flash',
      inputPerMTok: 0.1,
      outputPerMTok: 0.4,
      bestFor: 'Cheap.',
      isDefault: false,
      priceIsEstimate: true,
      available: true,
      unavailableReason: null,
      estimate: { inputTokens: 80000, outputTokens: 20000, costUsd: 0.016 },
    },
  ],
};

const run = (overrides: Partial<RunSummary> = {}): RunSummary => ({
  id: 'r_1',
  path: 'src/Lab/Beta.cs',
  state: 'working',
  reason: null,
  attempt: 2,
  maxAttempts: 5,
  lastPct: 71.4,
  targetPct: 85,
  model: 'glm-5.3:cloud',
  tokens: 1000,
  costUsd: 0.12,
  branch: null,
  createdAt: '',
  updatedAt: '',
  active: true,
  ...overrides,
});

/** The file at 66.7% with an 80% default threshold, opened by an admin. */
function open(extra: Record<string, (method: string) => Response> = {}, fileOverrides = {}) {
  const calls = stubCoverageApi({
    '/api/coverage/tree': () => jsonResponse(sampleTree),
    '/api/coverage/files': () => jsonResponse(detail(fileOverrides)),
    '/api/coverage/models': () => jsonResponse(models),
    ...extra,
  });
  renderWithProviders(<CoveragePage />, {
    route: '/coverage?file=src%2FLab%2FBeta.cs',
    session: makeSession('FIRM_ADMIN'),
  });
  return calls;
}

async function setThreshold(value: string) {
  const input = await screen.findByLabelText('Threshold (%)');
  await userEvent.clear(input);
  await userEvent.type(input, value);
  await userEvent.click(screen.getByRole('button', { name: 'Save' }));
}

describe('threshold control', () => {
  it('saves a lowered threshold at once, with no dialog', async () => {
    const calls = open({ '/api/coverage/thresholds': () => jsonResponse({}) });

    await setThreshold('50');

    expect(calls.some((c) => c.method === 'PUT' && c.url.includes('/thresholds'))).toBe(true);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('saves a raise that coverage already meets, with no dialog', async () => {
    const calls = open(
      { '/api/coverage/thresholds': () => jsonResponse({}) },
      { summary: { ...detail().summary, pct: 91, threshold: 80 } },
    );

    await setThreshold('90');

    expect(calls.some((c) => c.method === 'PUT')).toBe(true);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('confirms a raise above coverage, then asks for a model before starting', async () => {
    const calls = open({ '/api/coverage/runs': () => jsonResponse(run({ state: 'submitted' })) });

    await setThreshold('85');

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('66.7%');
    expect(dialog).toHaveTextContent('85%');
    expect(dialog).toHaveTextContent('agent run');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Continue' }));

    // The default model is preselected; its estimate and the cap are shown.
    const glm = await within(dialog).findByRole('radio', { name: /^GLM 5\.3\s*recommended/ });
    expect(glm).toBeChecked();
    expect(dialog).toHaveTextContent('$0.092');
    expect(dialog).toHaveTextContent('prices are estimates');
    expect(dialog).toHaveTextContent('$2.00 or 400,000 tokens');

    await userEvent.click(within(dialog).getByRole('radio', { name: /GLM 5\.3 Flash/ }));
    expect(dialog).toHaveTextContent('$0.016');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Start run' }));

    expect(calls.some((c) => c.method === 'POST' && c.url === '/api/coverage/runs')).toBe(true);
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('changes nothing when the confirmation is cancelled', async () => {
    const calls = open();

    await setThreshold('85');
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(calls.some((c) => c.method !== 'GET')).toBe(false);
  });

  it('changes nothing when the model picker is cancelled', async () => {
    const calls = open();

    await setThreshold('85');
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Continue' }));
    await within(dialog).findByRole('radio', { name: /^GLM 5\.3\s*recommended/ });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(calls.some((c) => c.method !== 'GET')).toBe(false);
  });

  it('does not let an unavailable model be picked', async () => {
    open();

    await setThreshold('85');
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Continue' }));

    const kimi = await within(dialog).findByRole('radio', { name: /Kimi K3/ });
    expect(kimi).toBeDisabled();
    expect(dialog).toHaveTextContent('Not available to this account.');
  });

  it('keeps Start disabled until a model is selected', async () => {
    open({
      '/api/coverage/models': () =>
        jsonResponse({ ...models, models: models.models.map((m) => ({ ...m, isDefault: false })) }),
    });

    await setThreshold('85');
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Continue' }));
    await within(dialog).findByRole('radio', { name: /GLM 5\.3 Flash/ });

    expect(within(dialog).getByRole('button', { name: 'Start run' })).toBeDisabled();
  });

  it('says the agent is unavailable and leaves the threshold alone', async () => {
    open({ '/api/coverage/runs': () => jsonResponse({ type: 'agent_unavailable' }, 503) });

    await setThreshold('85');
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Continue' }));
    await within(dialog).findByRole('radio', { name: /^GLM 5\.3\s*recommended/ });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Start run' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'The test agent is unavailable. The threshold was not changed.',
    );
  });

  it('is locked and shows the run while one is active', async () => {
    open({}, { run: run() });

    const status = await screen.findByRole('status', { name: 'Run status' });
    expect(status).toHaveTextContent('Working');
    expect(status).toHaveTextContent('attempt 2/5');
    expect(status).toHaveTextContent('71.4%');
    expect(screen.queryByLabelText('Threshold (%)')).toBeNull();
    expect(screen.getByText(/locked while a run is active/)).toBeInTheDocument();
  });

  it('is not offered to someone who is not an admin', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/files': () => jsonResponse(detail()),
    });

    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    expect(await screen.findByRole('region', { name: 'File src/Lab/Beta.cs' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Threshold (%)')).toBeNull();
  });
});
