import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import {
  agentFetch,
  controlledStreamResponse,
  jsonResponse,
  renderChat,
  run,
  streamResponse,
} from '@maf/testing';
import historyPlugin from './index';
import type { ConversationSummary } from './types';

const summary = (id: string, title = `Title ${id}`): ConversationSummary => ({
  conversationId: id,
  title,
  createdAt: new Date(Date.now() - 3600_000).toISOString(),
  lastActivityAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  turnCount: 1,
});

const conversation = summary('c-old', 'Fee schedules');

const detail = {
  conversationId: 'conv-7',
  title: 'Title conv-7',
  createdAt: '2026-09-19T09:00:00Z',
  lastActivityAt: '2026-09-19T09:05:00Z',
  turns: [
    {
      turnId: 't1',
      question: 'What if a fee schedule is missing?',
      answer: 'Assign the schedule and re-run.',
      createdAt: '2026-09-19T09:00:00Z',
      toolCalls: [],
      sources: [],
      feedbackKinds: [],
    },
  ],
};

/** The chat with the list in use, over a runtime whose runs stay open until the test closes them. */
function chat(history: ConversationSummary[]) {
  const streams: ReturnType<typeof controlledStreamResponse>[] = [];
  const runtime = agentFetch(
    vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations'))
        return jsonResponse({ conversations: history, nextCursor: null });
      if (url === '/api/chat') {
        const stream = controlledStreamResponse();
        streams.push(stream);
        return stream.response;
      }
      return streamResponse([]);
    }),
  );
  const fetchMock = vi.fn((url: string, init?: RequestInit) => runtime(url, init));
  vi.stubGlobal('fetch', fetchMock);
  renderChat([historyPlugin]);
  const stops = () => fetchMock.mock.calls.filter(([url]) => /\/agent\/chat\/stop\//.test(url));
  return { streams, stops };
}

async function ask(text = 'why did the fee go up?') {
  await userEvent.type(screen.getByLabelText('Message'), text);
  await userEvent.click(screen.getByRole('button', { name: 'Send' }));
}

// The list inside the core's chat: the chat draws the sidebar around it and the list fills it.
describe('ChatPage with the conversation list', () => {
  it('shows the list in a sidebar named after it, the open conversation marked, and collapses it to a rail', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) => {
          if (url.startsWith('/api/conversations?'))
            return jsonResponse({ conversations: [summary('conv-7')], nextCursor: null });
          if (url === '/api/conversations/conv-7') return jsonResponse(detail);
          return jsonResponse({}, 404);
        }),
      ),
    );

    renderChat([historyPlugin], { route: '/chat/conv-7' });
    const sidebar = screen.getByRole('navigation', { name: 'History' });
    expect(within(sidebar).getByRole('heading', { name: 'History' })).toBeInTheDocument();
    expect(await within(sidebar).findByRole('button', { name: /^Title conv-7/ })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(screen.getByRole('button', { name: 'Show history' })).toHaveAttribute(
      'aria-controls',
      sidebar.id,
    );

    // Collapsed, the list keeps its search; expanded again, it is still there.
    await userEvent.type(within(sidebar).getByLabelText('Search conversations'), 'fee');
    await userEvent.click(within(sidebar).getByRole('button', { name: 'Collapse history' }));
    expect(within(sidebar).getByLabelText('Search conversations')).not.toBeVisible();
    await userEvent.click(within(sidebar).getByRole('button', { name: 'Expand history' }));
    expect(within(sidebar).getByLabelText('Search conversations')).toHaveValue('fee');
  });

  it('opens a conversation from the list, and reads itself again once an answer is in', async () => {
    let listCalls = 0;
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) {
        listCalls += 1;
        return jsonResponse({
          conversations:
            listCalls === 1 ? [summary('conv-7')] : [summary('conv-new'), summary('conv-7')],
          nextCursor: null,
        });
      }
      if (url === '/api/conversations/conv-7') return jsonResponse(detail);
      if (url === '/api/chat')
        return streamResponse([run.delta('Hello.'), run.done('conv-new', 't1')]);
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat([historyPlugin], { route: '/chat' });
    await userEvent.click(await screen.findByRole('button', { name: /^Title conv-7/ }));
    expect(await screen.findByText('Assign the schedule and re-run.')).toBeInTheDocument();

    await ask('and then?');
    await screen.findByText('Hello.');
    expect(await screen.findByRole('button', { name: /^Title conv-new/ })).toBeInTheDocument();
  });

  it('deleting the conversation on screen starts a new one', async () => {
    let deleted = false;
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string, init?: RequestInit) => {
          if (init?.method === 'DELETE') {
            deleted = true;
            return jsonResponse(undefined, 204);
          }
          if (url.startsWith('/api/conversations?'))
            return jsonResponse({
              conversations: deleted ? [] : [summary('conv-7')],
              nextCursor: null,
            });
          if (url === '/api/conversations/conv-7') return jsonResponse(detail);
          return jsonResponse({}, 404);
        }),
      ),
    );

    renderChat([historyPlugin], { route: '/chat/conv-7' });
    expect(await screen.findAllByTestId('assistant-turn')).toHaveLength(1);
    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Title conv-7' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }),
    );

    await waitFor(() => expect(screen.queryAllByTestId('assistant-turn')).toHaveLength(0));
    expect(await screen.findByText('No conversations yet.')).toBeInTheDocument();
  });

  it('leaves the run going when Esc cancels a rename', async () => {
    const { streams, stops } = chat([conversation]);
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(run.delta('Still going')));
    await screen.findByText('Still going');

    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Fee schedules' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Rename' }));
    await userEvent.type(screen.getByLabelText('Conversation title'), ' x{Escape}');

    expect(screen.queryByLabelText('Conversation title')).not.toBeInTheDocument();
    expect(screen.getByTestId('stop-hint')).toHaveTextContent('Esc to stop');
    expect(screen.getByRole('button', { name: 'Answering…' })).toBeDisabled();
    expect(screen.queryByTestId('turn-stopped')).not.toBeInTheDocument();
    expect(stops()).toHaveLength(0);
  });
});
