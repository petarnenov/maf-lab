import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderWithProviders } from '../test';
import { AdminOverview } from './AdminOverview';

describe('Administration overview', () => {
  it('shows tenant usage counts and domains', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ turns: 7, activeUsers: 2, domains: { weather: 4 } })),
    );
    renderWithProviders(<AdminOverview platform={false} />, {
      session: makeSession('TENANT_ADMIN'),
    });
    expect(await screen.findByText('7 turns · 2 active users')).toBeInTheDocument();
    expect(screen.getByText('weather: 4 turns')).toBeInTheDocument();
  });
  it('shows the current organization permission audit', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        jsonResponse({
          actions: [
            {
              id: 1,
              at: '2026-10-09T00:00:00Z',
              actor: 'operator',
              action: 'plugin.allowance',
              arguments: 'plugin=weather,allowed=True',
              outcome: 'changed',
            },
          ],
          nextCursor: null,
        }),
      ),
    );
    renderWithProviders(<AdminOverview platform />, { session: makeSession('PLATFORM_ADMIN') });
    expect(await screen.findByText('plugin=weather,allowed=True')).toBeInTheDocument();
    expect(screen.getByText('operator')).toBeInTheDocument();
  });
});
