import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';

describe('admin routes', () => {
  it('denies a USER opening /admin/feedback', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderWithProviders(<App />, { session: makeSession('USER'), route: '/admin/feedback' });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
    expect(screen.queryByText('Feedback review queue')).not.toBeInTheDocument();
  });

  it('denies a READ_ONLY user opening /admin/feedback', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderWithProviders(<App />, { session: makeSession('READ_ONLY'), route: '/admin/feedback' });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
  });

  it('lets a TENANT_ADMIN open /admin/feedback', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderWithProviders(<App />, {
      session: makeSession('TENANT_ADMIN'),
      route: '/admin/feedback',
    });
    expect(await screen.findByText('Feedback review queue')).toBeInTheDocument();
  });
});
