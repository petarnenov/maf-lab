import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import { IndexAdminPage } from './IndexAdminPage';

describe('IndexAdminPage', () => {
  it('shows drift, model_version distribution, and polls a started indexing job', async () => {
    let jobPolls = 0;
    const runs: string[] = [];
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/platform/index/corpora')
        return jsonResponse([{ name: 'billing', hasGraph: true }]);
      if (url === '/api/platform/index/status?corpus=billing')
        return jsonResponse({
          modelVersions: [
            { modelVersion: 'nomic-embed-text@v1', chunks: 1200 },
            { modelVersion: 'next-embed@v2', chunks: 300 },
          ],
          activeDenseVector: 'dense_v1',
          currentJob: null,
        });
      if (url === '/api/platform/index/drift?corpus=billing')
        return jsonResponse({
          totalDocuments: 40,
          staleDocuments: 2,
          stalePercent: 5,
          stale: [
            {
              docId: 'd1',
              sourcePath: 'firm-a/docs/a.md',
              sourceUpdatedAt: '2026-09-19T10:00:00Z',
              indexedUpdatedAt: '2026-09-18T10:00:00Z',
            },
          ],
          missingFromIndex: [],
          graph: {
            available: true,
            reason: null,
            outOfSync: 3,
            outOfSyncPercent: 7.5,
            missingFromGraph: ['firm-a/docs/new.md'],
            behind: ['firm-a/docs/a.md', 'firm-a/docs/b.md'],
            notInCorpus: [],
          },
        });
      if (url === '/api/platform/index/run' && init?.method === 'POST') {
        runs.push(String(init.body));
        return jsonResponse(
          { jobId: 'j1', kind: 'index', state: 'running', startedAt: '2026-09-19T10:00:00Z' },
          202,
        );
      }
      if (url === '/api/platform/jobs/j1') {
        jobPolls++;
        return jsonResponse({
          jobId: 'j1',
          kind: 'index',
          state: 'succeeded',
          startedAt: '2026-09-19T10:00:00Z',
          summary: '40 documents',
        });
      }
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<IndexAdminPage />, { session: makeSession('PLATFORM_ADMIN') });

    expect(await screen.findByTestId('drift-percent')).toHaveTextContent('5.0%');
    // One corpus offered: named, with no choice to make.
    expect(screen.getByTestId('corpus')).toHaveTextContent('Corpus: billing');
    expect(screen.queryByRole('combobox', { name: 'Corpus' })).not.toBeInTheDocument();
    expect(screen.getByTestId('drift-graph')).toHaveTextContent('Graph: 3 of 40 out of sync');
    expect(await screen.findByText('nomic-embed-text@v1')).toBeInTheDocument();
    expect(screen.getByText('300')).toBeInTheDocument();
    expect(screen.getByText('firm-a/docs/a.md')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Run indexing' }));
    expect(await screen.findByTestId('job-status')).toHaveTextContent('succeeded');
    expect(jobPolls).toBeGreaterThan(0);
    expect(runs.map((b) => JSON.parse(b))).toEqual([{ corpus: 'billing' }]);
  });

  it('says the graph is unavailable without turning the drift card into an error', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url === '/api/platform/index/corpora')
          return jsonResponse([{ name: 'billing', hasGraph: true }]);
        if (url === '/api/platform/index/status?corpus=billing')
          return jsonResponse({
            modelVersions: [],
            activeDenseVector: 'dense_v3',
            currentJob: null,
          });
        if (url === '/api/platform/index/drift?corpus=billing')
          return jsonResponse({
            totalDocuments: 40,
            staleDocuments: 0,
            stalePercent: 0,
            stale: [],
            missingFromIndex: [],
            graph: {
              available: false,
              reason: 'unreachable',
              outOfSync: 0,
              outOfSyncPercent: 0,
              missingFromGraph: [],
              behind: [],
              notInCorpus: [],
            },
          });
        return jsonResponse({}, 404);
      }),
    );

    renderWithProviders(<IndexAdminPage />, { session: makeSession('PLATFORM_ADMIN') });

    expect(
      await screen.findByRole('progressbar', { name: 'Checking the index and the graph…' }),
    ).toBeInTheDocument();
    expect(await screen.findByTestId('drift-percent')).toHaveTextContent('0.0%');
    expect(screen.getByTestId('drift-graph')).toHaveTextContent('Graph: unavailable');
    expect(screen.queryByText('Unavailable')).not.toBeInTheDocument();
  });

  it('offers the installed corpora and reads and runs the one picked', async () => {
    const reads: string[] = [];
    const runs: string[] = [];
    const drift = (graph: object | null) => ({
      totalDocuments: 4,
      staleDocuments: 0,
      stalePercent: 0,
      stale: [],
      missingFromIndex: [],
      graph,
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        reads.push(url);
        if (url === '/api/platform/index/corpora')
          return jsonResponse([
            { name: 'billing', hasGraph: true },
            { name: 'portfolio', hasGraph: false },
          ]);
        if (url.startsWith('/api/platform/index/status'))
          return jsonResponse({
            modelVersions: [],
            activeDenseVector: 'dense_v3',
            currentJob: null,
          });
        if (url === '/api/platform/index/drift?corpus=billing')
          return jsonResponse(
            drift({
              available: true,
              reason: null,
              outOfSync: 0,
              outOfSyncPercent: 0,
              missingFromGraph: [],
              behind: [],
              notInCorpus: [],
            }),
          );
        if (url === '/api/platform/index/drift?corpus=portfolio')
          return jsonResponse(
            drift({
              available: false,
              reason: 'not-built',
              outOfSync: 0,
              outOfSyncPercent: 0,
              missingFromGraph: [],
              behind: [],
              notInCorpus: [],
            }),
          );
        if (url === '/api/platform/index/run' && init?.method === 'POST') {
          runs.push(String(init.body));
          return jsonResponse(
            { jobId: 'j2', kind: 'index', state: 'succeeded', startedAt: '2026-10-07T10:00:00Z' },
            202,
          );
        }
        if (url === '/api/platform/jobs/j2')
          return jsonResponse({
            jobId: 'j2',
            kind: 'index',
            state: 'succeeded',
            startedAt: '2026-10-07T10:00:00Z',
            summary: 'portfolio: indexed 4',
          });
        return jsonResponse({}, 404);
      }),
    );

    renderWithProviders(<IndexAdminPage />, { session: makeSession('PLATFORM_ADMIN') });

    expect(await screen.findByTestId('drift-graph')).toHaveTextContent('Graph: 0 of 4 out of sync');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Corpus' }), 'portfolio');
    await waitFor(() =>
      expect(screen.getByTestId('drift-graph')).toHaveTextContent(
        'Graph: not built for this corpus',
      ),
    );
    expect(reads).toContain('/api/platform/index/status?corpus=portfolio');

    await userEvent.click(screen.getByRole('button', { name: 'Run indexing' }));
    expect(await screen.findByTestId('job-status')).toHaveTextContent('portfolio: indexed 4');
    expect(runs.map((b) => JSON.parse(b))).toEqual([{ corpus: 'portfolio' }]);
  });

  it('says when no installed plugin declares a corpus, and offers nothing to run', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url === '/api/platform/index/corpora' ? jsonResponse([]) : jsonResponse({}, 404),
      ),
    );

    renderWithProviders(<IndexAdminPage />, { session: makeSession('PLATFORM_ADMIN') });

    expect(await screen.findByTestId('no-corpus')).toHaveTextContent(
      'No installed plugin declares a corpus.',
    );
    expect(screen.getByRole('button', { name: 'Run indexing' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Run migration' })).toBeDisabled();
  });
});
