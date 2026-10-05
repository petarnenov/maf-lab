import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { EvalsPage } from '../evals/EvalsPage';
import { JevPage } from '../jev/JevPage';
import { TelemetryPage } from '../telemetry/TelemetryPage';
import { hangingFetch } from '../test/hangingFetch';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';

/**
 * Reports still loading stop on Esc (stop-anything). These read Prometheus, stored traces and report files — nothing
 * paid — but everything can be stopped: the request is aborted, the page says so while it loads, and shows no error.
 */
describe('reports still loading stop on Esc', () => {
  it.each([
    ['telemetry', () => <TelemetryPage />, (url: string) => url.startsWith('/api/telemetry')],
    ['Jev statistics', () => <JevPage />, (url: string) => url.startsWith('/api/admin/jev-stats')],
    ['evals', () => <EvalsPage />, (url: string) => url.startsWith('/api/evals/reports')],
  ])('the %s screen', async (_, page, holds) => {
    const held = hangingFetch(holds, () => jsonResponse([]));
    renderWithProviders(page(), { session: makeSession('FIRM_ADMIN') });
    await waitFor(() => expect(held.length).toBeGreaterThan(0));
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');

    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(held.every((h) => h.signal.aborted)).toBe(true));
    expect(screen.queryByRole('alert')).toBeNull();
    await waitFor(() => expect(screen.queryByTestId('stop-hint')).toBeNull());
  });
});
