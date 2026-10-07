import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders } from '@maf/testing';
import { IndexAdminPage } from './IndexAdminPage';

const corpora = [{ name: 'billing', hasGraph: true }];
const status = { modelVersions: [], activeDenseVector: 'dense_v3', currentJob: null };
const drift = {
  totalDocuments: 10,
  staleDocuments: 0,
  stalePercent: 0,
  stale: [],
  missingFromIndex: [],
};

/**
 * The index screen over an api whose job runs until it is cancelled: the cancel route moves it to canceled, and the
 * job's own state then says how far it got.
 */
function screenWithJob(kind: 'index' | 'migrate') {
  let canceled = false;
  const cancels: string[] = [];
  const job = (state: string, summary?: string) => ({
    jobId: 'j1',
    kind,
    state,
    startedAt: '2026-10-04T10:00:00Z',
    summary,
  });
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/admin/index/corpora') return jsonResponse(corpora);
      if (url === '/api/admin/index/status?corpus=billing') return jsonResponse(status);
      if (url === '/api/admin/index/drift?corpus=billing') return jsonResponse(drift);
      if (
        url === `/api/admin/index/${kind === 'index' ? 'run' : 'migrate'}` &&
        init?.method === 'POST'
      )
        return jsonResponse(job('running'), 202);
      if (url === '/api/admin/jobs/j1/cancel' && init?.method === 'POST') {
        cancels.push(url);
        canceled = true;
        return jsonResponse(job('canceled', 'Canceled by an administrator.'), 202);
      }
      if (url === '/api/admin/jobs/j1')
        return jsonResponse(
          canceled
            ? job('canceled', 'Canceled by an administrator. It had indexed 3 of 10 documents.')
            : job('running'),
        );
      return jsonResponse({}, 404);
    }),
  );
  renderWithProviders(<IndexAdminPage />);
  return { cancels };
}

describe('IndexAdminPage: Esc stops what it started', () => {
  it.each([
    ['index', 'Run indexing'],
    ['migrate', 'Run migration'],
  ] as const)(
    'stops a %s job through its cancel route and shows how far it got',
    async (kind, button) => {
      const { cancels } = screenWithJob(kind);
      await userEvent.click(await screen.findByRole('button', { name: button }));
      expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');

      await userEvent.keyboard('{Escape}');

      await waitFor(() => expect(cancels).toEqual(['/api/admin/jobs/j1/cancel']));
      expect(await screen.findByTestId('job-status')).toHaveTextContent(
        'canceled — Canceled by an administrator. It had indexed 3 of 10 documents.',
      );
      expect(screen.queryByTestId('stop-hint')).not.toBeInTheDocument();
      // A second Esc with nothing running stops nothing more.
      await userEvent.keyboard('{Escape}');
      expect(cancels).toHaveLength(1);
    },
  );

  it('aborts a drift report that is still loading', async () => {
    const signals: AbortSignal[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        if (url === '/api/admin/index/corpora') return Promise.resolve(jsonResponse(corpora));
        if (url === '/api/admin/index/status?corpus=billing')
          return Promise.resolve(jsonResponse(status));
        if (init?.signal) signals.push(init.signal);
        return new Promise<Response>((_, reject) =>
          init?.signal?.addEventListener('abort', () =>
            reject(new DOMException('aborted', 'AbortError')),
          ),
        );
      }),
    );
    renderWithProviders(<IndexAdminPage />);
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');

    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(signals.some((s) => s.aborted)).toBe(true));
    expect(await screen.findByTestId('drift-stopped')).toHaveTextContent('Stopped.');
    expect(screen.getByRole('button', { name: 'Check again' })).toBeInTheDocument();
  });
});
