import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { jsonResponse, renderWithProviders } from '../test/render';

/** The screen is reachable where the navigation says it is, and only to someone signed in. */
describe('/telemetry', () => {
  it('is reached from the main navigation', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        jsonResponse({
          window: '1h',
          generatedAt: '',
          available: true,
          reason: null,
          traceUrl: null,
          panels: [],
        }),
      ),
    );

    renderWithProviders(<App />, { route: '/telemetry' });

    expect(await screen.findByRole('heading', { name: 'Telemetry' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Telemetry' })).toHaveAttribute('href', '/telemetry');
  });

  it('asks a signed-out visitor for a persona rather than showing numbers', () => {
    renderWithProviders(<App />, { route: '/telemetry', session: null });
    expect(screen.getByText(/Pick a dev persona/)).toBeInTheDocument();
  });
});
