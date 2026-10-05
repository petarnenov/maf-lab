import { EventType } from '@ag-ui/core';
import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { CoverageTree, RunSummary } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders, sse, streamResponse } from '../test/render';
import { CoveragePage } from './CoveragePage';
import { stubCoverageApi } from './CoveragePage.test';
import { liveRunIds, MAX_LIVE_ROWS } from './treeRuns';
import { file, sampleTree } from './treeModel.test';

const run = (overrides: Partial<RunSummary> = {}): RunSummary => ({
  id: 'r_1',
  path: 'src/Lab/Beta.cs',
  state: 'working',
  reason: null,
  attempt: 1,
  maxAttempts: 5,
  lastPct: 40,
  targetPct: 85,
  model: 'glm-5.3:cloud',
  tokens: 100,
  costUsd: 0.01,
  branch: null,
  createdAt: '2026-09-30T10:05:00Z',
  updatedAt: '2026-09-30T10:06:00Z',
  active: true,
  phase: 'generating',
  ...overrides,
});

const treeWith = (beta: RunSummary | null): CoverageTree => ({
  ...sampleTree,
  files: sampleTree.files.map((f) => (f.path === 'src/Lab/Beta.cs' ? { ...f, run: beta } : f)),
});

const row = async (name: string) => (await screen.findByRole('button', { name: new RegExp(`^${name}`) })) as HTMLElement;

describe('tree row run badge', () => {
  it('follows an active run live, with a moving marker', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(treeWith(run())),
      '/api/coverage/runs/r_1/events': () =>
        streamResponse([
          sse(EventType.RUN_STARTED, { threadId: 'testgen:r_1', runId: 'r_1' }),
          sse(EventType.STATE_SNAPSHOT, { snapshot: run({ attempt: 2, phase: 'building' }) }),
        ]),
    });

    renderWithProviders(<CoveragePage />, { route: '/coverage', session: makeSession('TENANT_ADMIN') });

    const beta = await row('Beta.cs');
    await vi.waitFor(() => expect(beta).toHaveTextContent('Working 2/5 · building'));
    expect(within(beta).getByRole('img', { name: 'Agent working' })).toBeInTheDocument();
  });

  it('calls attempt 0 the baseline', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(treeWith(run({ attempt: 0, phase: 'measuring' }))),
      '/api/coverage/runs/r_1/events': () => streamResponse([sse(EventType.STATE_SNAPSHOT, { snapshot: run({ attempt: 0, phase: 'measuring' }) })]),
    });

    renderWithProviders(<CoveragePage />, { route: '/coverage', session: makeSession('TENANT_ADMIN') });

    await vi.waitFor(async () => expect(await row('Beta.cs')).toHaveTextContent('Working baseline · measuring'));
  });

  it('keeps the last outcome, with why, once the run has ended', async () => {
    const ended = run({ state: 'completed_no_change', reason: 'budget', active: false, attempt: 2 });
    stubCoverageApi({ '/api/coverage/tree': () => jsonResponse(treeWith(ended)) });

    renderWithProviders(<CoveragePage />, { route: '/coverage', session: makeSession('TENANT_ADMIN') });

    const beta = await row('Beta.cs');
    const label = within(beta).getByText('no change');
    expect(label).toHaveAttribute('title', 'Last run: No change (budget)');
    expect(within(beta).queryByRole('img', { name: 'Agent working' })).toBeNull();
  });

  it('drops the outcome once the file is measured again', async () => {
    const ended = run({ state: 'failed', reason: 'deadline', active: false, updatedAt: '2026-09-30T09:00:00Z' });
    stubCoverageApi({ '/api/coverage/tree': () => jsonResponse(treeWith(ended)) });

    renderWithProviders(<CoveragePage />, { route: '/coverage', session: makeSession('TENANT_ADMIN') });

    expect(within(await row('Beta.cs')).queryByText('failed')).toBeNull();
  });

  it(`follows at most ${MAX_LIVE_ROWS} runs live, the most recent`, () => {
    const files = [1, 2, 3, 4].map((n) =>
      file(`src/F${n}.cs`, 10, { run: run({ id: `r_${n}`, updatedAt: `2026-09-30T10:0${n}:00Z` }) }),
    );
    files.push(file('src/Cand.cs', 10, { run: run({ id: 'r_c', state: 'candidate', updatedAt: '2026-09-30T11:00:00Z' }) }));

    expect([...liveRunIds(files)].sort()).toEqual(['r_2', 'r_3', 'r_4']);
  });
});
