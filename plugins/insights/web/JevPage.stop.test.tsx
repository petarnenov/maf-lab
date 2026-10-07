import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { hangingFetch, jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import { JevPage } from './JevPage';

/**
 * The Jev statistics still loading stop on Esc (stop-anything). They read stored turn records — nothing paid — but
 * everything can be stopped: the request is aborted, the page says so while it loads, and shows no error.
 */
describe('the Jev statistics still loading', () => {
  it('stop on Esc', async () => {
    const held = hangingFetch(
      (url: string) => url.startsWith('/api/admin/jev-stats'),
      () => jsonResponse([]),
    );
    renderWithProviders(<JevPage />, { session: makeSession('TENANT_ADMIN') });
    await waitFor(() => expect(held.length).toBeGreaterThan(0));
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');

    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(held.every((h) => h.signal.aborted)).toBe(true));
    expect(screen.queryByRole('alert')).toBeNull();
    await waitFor(() => expect(screen.queryByTestId('stop-hint')).toBeNull());
  });
});
