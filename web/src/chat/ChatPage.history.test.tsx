import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes, useLocation } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import type { ConversationDetail, ConversationPage } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { fixtureTrace } from '../monitor/fixtures';
import {
  jsonResponse,
  makeSession,
  renderWithProviders,
  sse,
  streamResponse,
} from '../test/render';
import { ChatPage } from './ChatPage';

const detail: ConversationDetail = {
  conversationId: 'conv-7',
  title: 'Missing fee schedule',
  createdAt: '2026-09-19T09:00:00Z',
  lastActivityAt: '2026-09-19T09:05:00Z',
  turns: [
    {
      turnId: 't1',
      question: 'What if a fee schedule is missing?',
      answer: 'Assign the schedule and re-run.',
      createdAt: '2026-09-19T09:00:00Z',
      toolCalls: [
        {
          callId: 'c1',
          toolName: 'search_documents',
          argumentSummary: 'query="fee"',
          outcome: 'ok',
          resultSummary: null,
          sourceCount: 1,
        },
      ],
      sources: [{ docId: 'd1', sectionPath: 'Fees', sourcePath: 'shared/fees.md', snippet: '' }],
      feedbackKinds: ['wrong_document'],
      traceAvailable: false,
    },
    {
      turnId: 't2',
      question: 'And then?',
      answer: 'Check the run status.',
      createdAt: '2026-09-19T09:05:00Z',
      toolCalls: [],
      sources: [],
      feedbackKinds: [],
      traceAvailable: true,
    },
  ],
};

const page = (...ids: string[]): ConversationPage => ({
  conversations: ids.map((id) => ({
    conversationId: id,
    title: `Title ${id}`,
    createdAt: '2026-09-19T09:00:00Z',
    lastActivityAt: '2026-09-19T09:05:00Z',
    turnCount: 2,
  })),
  nextCursor: null,
});

function Location() {
  return <div data-testid="location">{useLocation().pathname}</div>;
}

function renderChat(route: string, extra?: React.ReactNode) {
  return renderWithProviders(
    <>
      <Routes>
        <Route path="chat/:conversationId?" element={<ChatPage />} />
      </Routes>
      <Location />
      {extra}
    </>,
    { route },
  );
}

describe('ChatPage with history', () => {
  it('reloading /chat/:id restores the turns, feedback and trace availability', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) return jsonResponse(page('conv-7'));
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
    vi.stubGlobal('fetch', fetchMock);

    renderChat('/chat/conv-7');
    const turns = await screen.findAllByTestId('assistant-turn');
    expect(turns).toHaveLength(2);
    expect(within(turns[0]).getByText('Assign the schedule and re-run.')).toBeInTheDocument();
    // Old rows without a result summary fall back to the outcome.
    expect(within(turns[0]).getByTestId('tool-call-card')).toHaveAttribute(
      'data-state',
      'finished',
    );
    expect(within(turns[0]).getByRole('button', { name: '✓ Wrong document' })).toBeDisabled();
    expect(screen.getByText('What if a fee schedule is missing?')).toBeInTheDocument();

    // The latest turn's stored trace opens in the monitor.
    const monitor = screen.getByRole('region', { name: 'Behind the scenes' });
    expect(await within(monitor).findByText(`${fixtureTrace.length} events`)).toBeInTheDocument();

    // The expired trace is not fetched; the monitor says why.
    await userEvent.click(within(turns[0]).getByRole('button', { name: 'Behind the scenes' }));
    expect(within(monitor).getByRole('alert')).toHaveTextContent('Trace expired (kept 7 days)');
    expect(fetchMock.mock.calls.map((c) => c[0])).not.toContain('/api/turns/t1/trace');

    // The active conversation is highlighted in the sidebar.
    const history = screen.getByRole('navigation', { name: 'Conversation history' });
    expect(await within(history).findByRole('button', { name: /^Title conv-7/ })).toHaveAttribute(
      'aria-current',
      'page',
    );

    // Continuing sends the conversation id from the URL.
    fetchMock.mockImplementationOnce(async () =>
      streamResponse([
        sse('text_delta', { text: 'Third.' }),
        sse('done', { conversationId: 'conv-7', turnId: 't3' }),
      ]),
    );
    await userEvent.type(screen.getByLabelText('Message'), 'more');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('Third.');
    const [, init] = fetchMock.mock.calls.find(([u]) => u === '/api/chat') as unknown as [
      string,
      RequestInit,
    ];
    expect(JSON.parse(init.body as string)).toEqual({ message: 'more', conversationId: 'conv-7' });
  });

  it('shows "Conversation not found" for an unknown or deleted id', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url.startsWith('/api/conversations?')
          ? jsonResponse(page())
          : jsonResponse({ error: 'not_found' }, 404),
      ),
    );
    renderChat('/chat/gone');
    expect(await screen.findByText('Conversation not found.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Start a new conversation' }));
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
    expect(screen.queryByText('Conversation not found.')).not.toBeInTheDocument();
  });

  it('moves to /chat/{id} after the first answer and refreshes the list', async () => {
    let listCalls = 0;
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) {
        listCalls += 1;
        return jsonResponse(listCalls === 1 ? page() : page('conv-new'));
      }
      if (url === '/api/chat')
        return streamResponse([
          sse('text_delta', { text: 'Hello.' }),
          sse('done', { conversationId: 'conv-new', turnId: 't1' }),
        ]);
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);

    renderChat('/chat');
    expect(await screen.findByText('No conversations yet.')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Message'), 'hi');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('Hello.');

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/chat/conv-new'));
    expect(await screen.findByRole('button', { name: /^Title conv-new/ })).toBeInTheDocument();
    // Still the live turn: the page did not reload the conversation it already shows.
    expect(fetchMock.mock.calls.map((c) => c[0])).not.toContain('/api/conversations/conv-new');
    expect(screen.getAllByTestId('assistant-turn')).toHaveLength(1);
  });

  it('starting a new conversation is not undone by the cached one', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url.startsWith('/api/conversations?')) return jsonResponse(page('conv-7'));
        if (url === '/api/conversations/conv-7') return jsonResponse(detail);
        return jsonResponse({}, 404);
      }),
    );

    // Opening the conversation first is what primes the query cache — the cache is the trap.
    renderChat('/chat/conv-7');
    expect(await screen.findAllByTestId('assistant-turn')).toHaveLength(2);

    await userEvent.click(screen.getByRole('button', { name: '＋ New conversation' }));

    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
    expect(screen.queryAllByTestId('assistant-turn')).toHaveLength(0);
    expect(screen.queryByText('What if a fee schedule is missing?')).not.toBeInTheDocument();
    // The cached conversation must not creep back once the route has settled.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
    expect(screen.queryAllByTestId('assistant-turn')).toHaveLength(0);
  });

  it("switching persona clears the chat and shows only the new user's list", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        const auth = new Headers(init?.headers).get('Authorization') ?? '';
        if (url.startsWith('/api/conversations?'))
          return jsonResponse(auth.includes('FIRM_ADMIN') ? page('alice-1') : page('adam-1'));
        if (url === '/api/conversations/adam-1')
          return jsonResponse({ ...detail, conversationId: 'adam-1' });
        return jsonResponse({}, 404);
      }),
    );
    function SwitchPersona() {
      const { setSession } = useAuth();
      return (
        <button type="button" onClick={() => setSession(makeSession('FIRM_ADMIN'))}>
          switch
        </button>
      );
    }

    renderChat('/chat/adam-1', <SwitchPersona />);
    expect(await screen.findByRole('button', { name: /^Title adam-1/ })).toBeInTheDocument();
    expect(await screen.findAllByTestId('assistant-turn')).toHaveLength(2);

    await userEvent.click(screen.getByRole('button', { name: 'switch' }));
    expect(await screen.findByRole('button', { name: /^Title alice-1/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Title adam-1/ })).not.toBeInTheDocument();
    expect(screen.queryAllByTestId('assistant-turn')).toHaveLength(0);
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
  });
});
