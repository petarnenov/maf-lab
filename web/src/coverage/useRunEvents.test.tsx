import { EventType } from '@ag-ui/core';
import { act, screen } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { RunSummary } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders, sse, streamResponse } from '../test/render';
import { agentFetch } from '../test/agentFetch';
import { CoveragePage } from './CoveragePage';
import { detail } from './CoveragePage.test';
import { openRunStreams, useRunStream } from './runStream';
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

const state = (summary: RunSummary) => sse(EventType.STATE_SNAPSHOT, { snapshot: summary });

describe('run events (AG-UI)', () => {
  it('follows a run live and refreshes the file when it changes state', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      agentFetch(vi.fn(async (url: string) => {
        calls.push(url);
        if (url.startsWith('/api/coverage/tree')) return jsonResponse(sampleTree);
        if (url.startsWith('/api/coverage/files')) return jsonResponse(detail({ run: run() }));
        if (url.includes('/events'))
          return streamResponse([
            sse(EventType.RUN_STARTED, { threadId: 'testgen:r_1', runId: 'r_1' }),
            state(run({ attempt: 2, lastPct: 71.4 })),
            state(run({ attempt: 3, lastPct: 79.9, phase: 'building' })),
            state(run({ state: 'verifying', attempt: 3, lastPct: 86 })),
          ]);
        return jsonResponse({}, 404);
      })),
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
      agentFetch(vi.fn(async (url: string) => {
        calls.push(url);
        if (url.startsWith('/api/coverage/tree')) return jsonResponse(sampleTree);
        if (url.startsWith('/api/coverage/files'))
          return jsonResponse(detail({ run: run({ state: 'candidate', branch: 'test-agent/x' }) }));
        return jsonResponse({}, 404);
      })),
    );

    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    expect(await screen.findByRole('status', { name: 'Run status' })).toHaveTextContent('Candidate ready');
    expect(calls.some((u) => u.includes('/events'))).toBe(false);
  });

  describe('one stream per run, shared', () => {
    function Follower({ id }: { id: string }) {
      const { summary } = useRunStream(id);
      return <p data-testid="follower">{summary ? `attempt ${summary.attempt}` : 'waiting'}</p>;
    }

    function stubStream() {
      const opened: string[] = [];
      vi.stubGlobal(
        'fetch',
        agentFetch(vi.fn(async (url: string, init?: RequestInit) => {
          opened.push(url);
          // Held open until the subscribers leave, like a live run.
          const body = new ReadableStream<Uint8Array>({
            start(controller) {
              controller.enqueue(new TextEncoder().encode(state(run({ attempt: 2 }))));
              init?.signal?.addEventListener('abort', () => controller.error(new DOMException('aborted', 'AbortError')));
            },
          });
          return new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } });
        })),
      );
      return opened;
    }

    it('opens one request for two subscribers and closes it when both leave', async () => {
      const opened = stubStream();

      const view = renderWithProviders(
        <>
          <Follower id="r_1" />
          <Follower id="r_1" />
        </>,
        { session: makeSession('FIRM_ADMIN') },
      );

      await vi.waitFor(() => expect(screen.getAllByTestId('follower').map((p) => p.textContent)).toEqual(['attempt 2', 'attempt 2']));
      expect(opened).toHaveLength(1);
      expect(openRunStreams()).toBe(1);

      view.unmount();
      expect(openRunStreams()).toBe(0);
    });

    it('gives a late subscriber the current state at once', async () => {
      stubStream();
      function Later() {
        const [second, setSecond] = useState(false);
        return (
          <>
            <Follower id="r_1" />
            {second ? <Follower id="r_1" /> : <button onClick={() => setSecond(true)}>add</button>}
          </>
        );
      }
      const view = renderWithProviders(<Later />, { session: makeSession('FIRM_ADMIN') });
      await vi.waitFor(() => expect(screen.getByTestId('follower')).toHaveTextContent('attempt 2'));

      await act(async () => screen.getByRole('button', { name: 'add' }).click());

      // No wait: the second one reads the shared state on its first render.
      expect(screen.getAllByTestId('follower').map((p) => p.textContent)).toEqual(['attempt 2', 'attempt 2']);
      view.unmount();
    });
  });
});
