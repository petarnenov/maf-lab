import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { hangingFetch } from '../test/hangingFetch';
import { jsonResponse, renderWithProviders } from '../test/render';
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
});
