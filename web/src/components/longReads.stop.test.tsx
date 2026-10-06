import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { CompliancePage } from '../compliance/CompliancePage';
import { hangingFetch } from '../test/hangingFetch';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { TopologyPage } from '../topology/TopologyPage';

/** Slow reads stop on Esc (stop-anything): the request is aborted, and the page says it stopped. */
describe('long reads stop on Esc', () => {
  it('the topology probe', async () => {
    const held = hangingFetch(
      (url) => url === '/api/topology',
      () => jsonResponse({}, 404),
    );
    renderWithProviders(<TopologyPage />);
    await waitFor(() => expect(held).toHaveLength(1));
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');

    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(held[0].signal.aborted).toBe(true));
    expect(screen.queryByTestId('stop-hint')).toBeNull();
  });

  it('the audit chain check, which can be run again', async () => {
    const held = hangingFetch(
      (url) => url === '/api/admin/compliance/verify',
      () => jsonResponse({ actions: [], nextCursor: null }),
    );
    renderWithProviders(<CompliancePage />, { session: makeSession('TENANT_ADMIN') });
    await waitFor(() => expect(held).toHaveLength(1));

    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(held[0].signal.aborted).toBe(true));
    expect(await screen.findByTestId('chain-stopped')).toHaveTextContent('Stopped.');
    await userEvent.click(screen.getByRole('button', { name: 'Check again' }));
    await waitFor(() => expect(held).toHaveLength(2));
  });

  it('the compliance export, which downloads nothing', async () => {
    const held = hangingFetch(
      (url) =>
        url.startsWith('/api/admin/compliance/export') || url === '/api/admin/compliance/verify',
      () => jsonResponse({ actions: [], nextCursor: null }),
    );
    renderWithProviders(<CompliancePage />, { session: makeSession('TENANT_ADMIN') });
    // The chain check is held too; stop it first so the export is the only thing left running.
    await waitFor(() => expect(held).toHaveLength(1));
    await userEvent.keyboard('{Escape}');
    await screen.findByTestId('chain-stopped');

    await userEvent.click(screen.getByRole('button', { name: 'Download package' }));
    await waitFor(() =>
      expect(held.some((h) => h.url.startsWith('/api/admin/compliance/export'))).toBe(true),
    );
    await userEvent.keyboard('{Escape}');

    const exporting = held.find((h) => h.url.startsWith('/api/admin/compliance/export'))!;
    await waitFor(() => expect(exporting.signal.aborted).toBe(true));
    expect(await screen.findByTestId('export-stopped')).toHaveTextContent('nothing was downloaded');
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
