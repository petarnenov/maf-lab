import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { agentFetch, jsonResponse, renderChat } from '@maf/testing';
import { fixtureFrames, fixtureTrace } from './fixtures';
import monitorPlugin from './index';

const turn = (turnId: string, question: string, answer: string) => ({
  turnId,
  question,
  answer,
  createdAt: '2026-09-19T09:00:00Z',
  toolCalls: [],
  sources: [],
  feedbackKinds: [],
});

const detail = {
  conversationId: 'conv-7',
  title: 'Missing fee schedule',
  createdAt: '2026-09-19T09:00:00Z',
  lastActivityAt: '2026-09-19T09:05:00Z',
  turns: [
    turn('t1', 'What if a fee schedule is missing?', 'Assign the schedule and re-run.'),
    turn('t2', 'And then?', 'Check the run status.'),
  ],
};

const page = { conversations: [], nextCursor: null };

// A reopened conversation in the monitor: each turn's stored trace, read from the monitor's own route.
describe('ChatPage with the monitor, reopened', () => {
  it("loads the latest turn's stored trace, and says so when a turn's trace is no longer kept", async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) return jsonResponse(page);
      if (url === '/api/conversations/conv-7') return jsonResponse(detail);
      if (url === '/api/turns/t2/trace')
        return jsonResponse({
          turnId: 't2',
          conversationId: 'conv-7',
          createdAt: '',
          events: fixtureTrace,
        });
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat([monitorPlugin], { route: '/chat/conv-7' });
    const turns = await screen.findAllByTestId('assistant-turn');
    const monitor = screen.getByRole('region', { name: 'Behind the scenes' });
    expect(await within(monitor).findByText(`${fixtureTrace.length} events`)).toBeInTheDocument();

    // t1's trace is past the retention period: the route says 404 once, and the monitor says why.
    await userEvent.click(within(turns[0]).getByRole('button', { name: 'Behind the scenes' }));
    expect(await within(monitor).findByRole('alert')).toHaveTextContent(
      'Trace expired (kept 7 days)',
    );
    expect(fetchMock.mock.calls.filter((c) => c[0] === '/api/turns/t1/trace')).toHaveLength(1);
  });

  it('a reopened turn shows the AG-UI frames stored with its trace, and says when there are none', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) return jsonResponse(page);
      if (url === '/api/conversations/conv-7') return jsonResponse(detail);
      if (url === '/api/turns/t2/trace')
        return jsonResponse({
          turnId: 't2',
          conversationId: 'conv-7',
          createdAt: '',
          events: fixtureTrace,
          aguiFrames: fixtureFrames,
        });
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat([monitorPlugin], { route: '/chat/conv-7' });
    const monitor = screen.getByRole('region', { name: 'Behind the scenes' });
    await within(monitor).findByText(`${fixtureTrace.length} events`);

    await userEvent.click(within(monitor).getByRole('tab', { name: 'AG-UI' }));
    const rows = within(
      await within(monitor).findByRole('list', { name: 'AG-UI frames' }),
    ).getAllByRole('listitem');
    expect(rows).toHaveLength(fixtureFrames.length);
    expect(rows[0]).toHaveAttribute('data-type', 'RUN_STARTED');
  });

  it('a stored turn whose frames were never recorded says so', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) return jsonResponse(page);
      if (url === '/api/conversations/conv-7') return jsonResponse(detail);
      if (url === '/api/turns/t2/trace')
        return jsonResponse({
          turnId: 't2',
          conversationId: 'conv-7',
          createdAt: '',
          events: fixtureTrace,
          aguiFrames: null,
        });
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat([monitorPlugin], { route: '/chat/conv-7' });
    const monitor = screen.getByRole('region', { name: 'Behind the scenes' });
    await within(monitor).findByText(`${fixtureTrace.length} events`);

    await userEvent.click(within(monitor).getByRole('tab', { name: 'AG-UI' }));
    expect(await within(monitor).findByText(/were not recorded/)).toBeInTheDocument();
  });
});
