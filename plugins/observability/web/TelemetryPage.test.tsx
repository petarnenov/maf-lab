import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { hangingFetch, jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import { TelemetryPage } from './TelemetryPage';
import type { TelemetryReport } from './types';

const report = (overrides: Partial<TelemetryReport> = {}): TelemetryReport => ({
  window: '1h',
  generatedAt: '2026-09-22T10:00:00Z',
  available: true,
  reason: null,
  traceUrl: 'http://localhost:7171/jaeger',
  panels: [
    {
      id: 'turns',
      title: 'Turns by outcome',
      unit: 'turns',
      series: [
        { label: 'answered', value: 12 },
        { label: 'failed', value: 1 },
      ],
    },
    {
      id: 'instances',
      title: 'Turns by instance',
      unit: 'turns',
      series: [
        { label: 'api-1', value: 7 },
        { label: 'api-2', value: 5 },
      ],
    },
    { id: 'tokens', title: 'Tokens used', unit: 'tokens', series: [] },
  ],
  ...overrides,
});

describe('TelemetryPage', () => {
  it('shows what the stack measured, per period and per instance', async () => {
    const urls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        urls.push(url);
        return jsonResponse(report());
      }),
    );

    renderWithProviders(<TelemetryPage />, { session: makeSession('PLATFORM_ADMIN') });

    const turns = await screen.findByRole('region', { name: 'Turns by outcome' });
    expect(turns).toHaveTextContent('answered');
    expect(turns).toHaveTextContent('12');
    // Every number says which period it covers.
    expect(turns).toHaveTextContent('Last hour');

    const instances = screen.getByRole('region', { name: 'Turns by instance' });
    expect(instances).toHaveTextContent('api-1');
    expect(instances).toHaveTextContent('api-2');

    expect(urls[0]).toContain('/api/platform/telemetry?window=1h');
  });

  it('says a period has no data rather than showing it as zero', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse(report())),
    );

    renderWithProviders(<TelemetryPage />, { session: makeSession('PLATFORM_ADMIN') });

    const tokens = await screen.findByRole('region', { name: 'Tokens used' });
    expect(within(tokens).getByText('No data for this period.')).toBeInTheDocument();
    expect(tokens).not.toHaveTextContent('0 tokens');
  });

  it('asks the server for the period the user picked', async () => {
    const urls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        urls.push(url);
        return jsonResponse(report({ window: '24h' }));
      }),
    );

    renderWithProviders(<TelemetryPage />, { session: makeSession('PLATFORM_ADMIN') });
    await screen.findByRole('region', { name: 'Turns by outcome' });

    await userEvent.selectOptions(screen.getByLabelText('Period'), '24h');

    expect(urls.some((u) => u.includes('window=24h'))).toBe(true);
  });

  it('stays usable when the numbers cannot be read', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        jsonResponse(
          report({
            available: false,
            reason: 'The metrics store could not be reached.',
            panels: [],
          }),
        ),
      ),
    );

    renderWithProviders(<TelemetryPage />, { session: makeSession('PLATFORM_ADMIN') });

    expect(await screen.findByRole('alert')).toHaveTextContent('could not be reached');
    // The screen is still there, with its controls, rather than an empty chart.
    expect(screen.getByLabelText('Period')).toBeInTheDocument();
  });

  it('offers a way to open the trace store, and asks for a persona when signed out', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse(report())),
    );

    const { unmount } = renderWithProviders(<TelemetryPage />, {
      session: makeSession('PLATFORM_ADMIN'),
    });
    expect(await screen.findByRole('link', { name: 'Open the trace store' })).toHaveAttribute(
      'href',
      'http://localhost:7171/jaeger',
    );
    unmount();

    renderWithProviders(<TelemetryPage />, { session: null });
    expect(screen.getByText(/Pick a dev persona/)).toBeInTheDocument();
  });

  // Moved from the core's reports.stop and abort tests with the screen (stop-anything).
  it('stops its read on Esc, says so while it loads, and shows no error', async () => {
    const held = hangingFetch(
      (url) => url.startsWith('/api/platform/telemetry'),
      () => jsonResponse([]),
    );
    renderWithProviders(<TelemetryPage />, { session: makeSession('PLATFORM_ADMIN') });
    await waitFor(() => expect(held.length).toBeGreaterThan(0));
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');

    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(held.every((h) => h.signal.aborted)).toBe(true));
    expect(screen.queryByRole('alert')).toBeNull();
    await waitFor(() => expect(screen.queryByTestId('stop-hint')).toBeNull());
  });

  it('aborts its read when the page is left while it is still loading', async () => {
    const held = hangingFetch(
      (url) => url.startsWith('/api/platform/telemetry'),
      () => jsonResponse([]),
    );
    const page = renderWithProviders(<TelemetryPage />, { session: makeSession('PLATFORM_ADMIN') });
    await waitFor(() => expect(held.length).toBeGreaterThan(0));
    expect(held.every((h) => !h.signal.aborted)).toBe(true);

    page.unmount();

    await waitFor(() => expect(held.every((h) => h.signal.aborted)).toBe(true));
  });
});
