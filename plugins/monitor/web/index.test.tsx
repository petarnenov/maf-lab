import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders } from '@maf/testing';
import { fixtureTrace } from './fixtures';
import monitorPlugin from './index';

describe('the monitor plugin', () => {
  it("gives the review queue a reviewed turn's stored trace", async () => {
    const fetchMock = vi.fn(async (url: string) =>
      url === '/api/turns/turn-42/trace'
        ? jsonResponse({
            turnId: 'turn-42',
            conversationId: 'conv-1',
            createdAt: '',
            events: fixtureTrace,
          })
        : jsonResponse({}, 404),
    );
    vi.stubGlobal('fetch', fetchMock);
    const [panel] = monitorPlugin.reviewPanels ?? [];
    expect(panel.label).toBe('trace');

    renderWithProviders(<>{panel.render({ turnId: 'turn-42' })}</>);

    const region = await screen.findByRole('region', { name: 'Behind the scenes — turn turn-42' });
    expect(await within(region).findByText(`${fixtureTrace.length} events`)).toBeInTheDocument();
    expect(fetchMock.mock.calls.map((c) => c[0])).toContain('/api/turns/turn-42/trace');
  });

  it('says so when the reviewed turn has no trace any more', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({}, 404)),
    );
    const [panel] = monitorPlugin.reviewPanels ?? [];

    renderWithProviders(<>{panel.render({ turnId: 'turn-old' })}</>);

    expect(await screen.findByRole('alert')).toHaveTextContent(/older than the retention period/);
  });
});
