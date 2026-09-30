import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { RunSummary } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders, streamResponse } from '../test/render';
import { CoveragePage } from './CoveragePage';
import { detail } from './CoveragePage.test';
import { sampleTree } from './treeModel.test';

const run = (overrides: Partial<RunSummary> = {}): RunSummary => ({
  id: 'r_1',
  path: 'src/Lab/Beta.cs',
  state: 'working',
  reason: null,
  attempt: 1,
  maxAttempts: 5,
  lastPct: 62,
  targetPct: 85,
  model: 'glm-5.3:cloud',
  tokens: 100,
  costUsd: 0.01,
  branch: null,
  createdAt: '',
  updatedAt: '',
  active: true,
  ...overrides,
});

const frame = (event: string, summary: RunSummary) => `event: ${event}\ndata: ${JSON.stringify(summary)}\n\n`;

describe('run events', () => {
  it('follows a run live and refreshes the file when it changes state', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        calls.push(url);
        if (url.startsWith('/api/coverage/tree')) return jsonResponse(sampleTree);
        if (url.startsWith('/api/coverage/files')) return jsonResponse(detail({ run: run() }));
        if (url.includes('/events'))
          return streamResponse([
            frame('snapshot', run({ attempt: 2, lastPct: 71.4 })),
            frame('update', run({ attempt: 3, lastPct: 79.9 })),
            frame('update', run({ state: 'verifying', attempt: 3, lastPct: 86 })),
          ]);
        return jsonResponse({}, 404);
      }),
    );

    renderWithProviders(<CoveragePage />, {
      route: '/coverage?file=src%2FLab%2FBeta.cs',
      session: makeSession('FIRM_ADMIN'),
    });

    const status = await screen.findByRole('status', { name: 'Run status' });
    await vi.waitFor(() => expect(status).toHaveTextContent('Verifying'));
    expect(status).toHaveTextContent('attempt 3/5');
    expect(status).toHaveTextContent('86.0%');
    // Working → verifying is a change of state: the file was fetched again.
    await vi.waitFor(() => expect(calls.filter((u) => u.startsWith('/api/coverage/files')).length).toBeGreaterThan(1));
  });

  it('does not follow a candidate, which waits for a person', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        calls.push(url);
        if (url.startsWith('/api/coverage/tree')) return jsonResponse(sampleTree);
        if (url.startsWith('/api/coverage/files'))
          return jsonResponse(detail({ run: run({ state: 'candidate', branch: 'test-agent/x' }) }));
        return jsonResponse({}, 404);
      }),
    );

    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    expect(await screen.findByRole('status', { name: 'Run status' })).toHaveTextContent('Candidate ready');
    expect(calls.some((u) => u.includes('/events'))).toBe(false);
  });
});
