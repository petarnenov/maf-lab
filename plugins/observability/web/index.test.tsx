import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders } from '@maf/testing';
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
    expect(observability.nav).toEqual([{ label: 'Telemetry', to: '/telemetry' }]);
    const [route] = observability.routes ?? [];

    expect(route.path).toBe('telemetry');
    renderWithProviders(<>{route.element}</>);

    expect(await screen.findByRole('heading', { name: 'Telemetry' })).toBeInTheDocument();
  });

  it('exports no spans outside a built app unless an endpoint is named', () => {
    // A test is not a built app and names none: activating starts nothing, so there is nothing to stop.
    expect(observability.activate?.()).toBeUndefined();
  });
});
