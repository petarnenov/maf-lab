import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { IndexAdminPage } from './IndexAdminPage';

describe('IndexAdminPage', () => {
  it('shows drift, model_version distribution, and polls a started indexing job', async () => {
    let jobPolls = 0;
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/admin/index/status')
        return jsonResponse({
          modelVersions: [
            { modelVersion: 'nomic-embed-text@v1', chunks: 1200 },
            { modelVersion: 'next-embed@v2', chunks: 300 },
          ],
          activeDenseVector: 'dense_v1',
          currentJob: null,
        });
      if (url === '/api/admin/index/drift')
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
        });
      if (url === '/api/admin/index/run' && init?.method === 'POST')
        return jsonResponse(
          { jobId: 'j1', kind: 'index', state: 'running', startedAt: '2026-09-19T10:00:00Z' },
          202,
        );
      if (url === '/api/admin/jobs/j1') {
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

    renderWithProviders(<IndexAdminPage />, { session: makeSession('FIRM_ADMIN') });

    expect(await screen.findByTestId('drift-percent')).toHaveTextContent('5.0%');
    expect(await screen.findByText('nomic-embed-text@v1')).toBeInTheDocument();
    expect(screen.getByText('300')).toBeInTheDocument();
    expect(screen.getByText('firm-a/docs/a.md')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Run indexing' }));
    expect(await screen.findByTestId('job-status')).toHaveTextContent('succeeded');
    expect(jobPolls).toBeGreaterThan(0);
  });
});
