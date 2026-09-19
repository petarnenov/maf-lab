import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';

describe('admin routes', () => {
  it('denies an ADVISOR opening /admin/index', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderWithProviders(<App />, { session: makeSession('ADVISOR'), route: '/admin/index' });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
    expect(screen.queryByText('Index administration')).not.toBeInTheDocument();
  });

  it('denies a READ_ONLY user opening /admin/feedback', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderWithProviders(<App />, { session: makeSession('READ_ONLY'), route: '/admin/feedback' });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
  });

  it('lets a FIRM_ADMIN open /admin/index', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url === '/api/admin/index/status')
          return jsonResponse({
            modelVersions: [],
            activeDenseVector: 'dense_v1',
            currentJob: null,
          });
        if (url === '/api/admin/index/drift')
          return jsonResponse({
            totalDocuments: 10,
            staleDocuments: 0,
            stalePercent: 0,
            stale: [],
            missingFromIndex: [],
          });
        return jsonResponse([]);
      }),
    );
    renderWithProviders(<App />, { session: makeSession('FIRM_ADMIN'), route: '/admin/index' });
    expect(await screen.findByText('Index administration')).toBeInTheDocument();
    expect(await screen.findByTestId('drift-percent')).toHaveTextContent('0.0%');
  });
});
