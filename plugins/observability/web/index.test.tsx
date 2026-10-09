import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderPluginApp, renderWithProviders } from '@maf/testing';
import observability from './index';

describe('the observability plugin', () => {
  it('gives the app its Telemetry screen and the link to it', async () => {
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
    expect(observability.nav).toEqual([
      { label: 'Telemetry', to: '/telemetry', platformAdminOnly: true },
    ]);
    const [route] = observability.routes ?? [];

    expect(route.path).toBe('telemetry');
    expect(route.platformAdmin).toBe(true);
    renderWithProviders(<>{route.element}</>, { session: makeSession('PLATFORM_ADMIN') });

    expect(await screen.findByRole('heading', { name: 'Telemetry' })).toBeInTheDocument();
  });

  it('exports no spans outside a built app unless an endpoint is named', () => {
    // A test is not a built app and names none: activating starts nothing, so there is nothing to stop.
    expect(observability.activate?.()).toBeUndefined();
  });

  it('contributes its screen to the platform dashboard', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ available: false, panels: [] })),
    );
    const [section] = observability.platformAdminSections ?? [];
    expect(section.id).toBe('telemetry');
    renderWithProviders(<>{section.render()}</>, { session: makeSession('PLATFORM_ADMIN') });
    expect(await screen.findByRole('heading', { name: 'Telemetry' })).toBeInTheDocument();
  });

  it.each(['USER', 'TENANT_ADMIN'] as const)(
    'guards its standalone screen from %s',
    async (role) => {
      renderPluginApp([observability], { route: '/telemetry', session: makeSession(role) });
      expect(screen.getByRole('alert')).toHaveTextContent('PLATFORM_ADMIN');
      expect(screen.queryByRole('link', { name: 'Telemetry' })).toBeNull();
      expect(screen.queryByRole('heading', { name: 'Telemetry' })).toBeNull();
    },
  );
});
