import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes, useLocation } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import type { ConversationDetail } from '../api/types';
import { useAuth } from '../auth/useAuth';
import {
  jsonResponse,
  makeSession,
  renderWithProviders,
  run,
  streamResponse,
} from '../test/render';
import { agentFetch } from '../test/agentFetch';
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
    },
    {
      turnId: 't2',
      question: 'And then?',
      answer: 'Check the run status.',
      createdAt: '2026-09-19T09:05:00Z',
      toolCalls: [],
      sources: [],
      feedbackKinds: [],
      reasoning: 'The failure code says FS-REQUIRED, so the schedule is missing.',
      reasoningMs: 200,
    },
  ],
};

const page = (...ids: string[]) => ({
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
  it('reloading /chat/:id restores the turns and their feedback', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) return jsonResponse(page('conv-7'));
      if (url === '/api/conversations/conv-7') return jsonResponse(detail);
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

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

    // With no plugin in use there is no side pane and no sidebar, and nothing that would open either.
    expect(screen.queryByRole('tablist', { name: 'Right pane' })).not.toBeInTheDocument();
    expect(within(turns[0]).queryByRole('button', { name: /behind the scenes/i })).toBeNull();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Show / })).not.toBeInTheDocument();

    // Continuing sends the conversation id from the URL.
    fetchMock.mockImplementationOnce(async () =>
      streamResponse([run.delta('Third.'), run.done('conv-7', 't3')]),
    );
    await userEvent.type(screen.getByLabelText('Message'), 'more');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('Third.');
    const [, init] = fetchMock.mock.calls.find(([u]) => u === '/api/chat') as unknown as [
      string,
      RequestInit,
    ];
    const body = JSON.parse(init.body as string);
    expect(body.threadId).toBe('conv-7');
    expect(body.messages[0].content).toBe('more');
  });

  it('keeps a failed turn on screen with its error, in the same conversation', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations?')) return jsonResponse(page('conv-7'));
      if (url === '/api/conversations/conv-7') return jsonResponse(detail);
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat('/chat/conv-7');
    expect(await screen.findAllByTestId('assistant-turn')).toHaveLength(2);

    fetchMock.mockImplementationOnce(async () =>
      streamResponse([
        run.started('conv-7'),
        run.error('The assistant could not complete this answer. Please try again.'),
      ]),
    );
    await userEvent.type(screen.getByLabelText('Message'), 'tell me about frogs');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const error = await screen.findByTestId('turn-error');
    expect(error).toHaveTextContent('could not complete this answer');
    expect(screen.getByText('tell me about frogs')).toBeInTheDocument();
    expect(screen.getAllByTestId('assistant-turn')).toHaveLength(3);
    expect(screen.getByTestId('location')).toHaveTextContent('/chat/conv-7');
    // The stored conversation is not loaded again over the live turn.
    expect(fetchMock.mock.calls.filter(([u]) => u === '/api/conversations/conv-7')).toHaveLength(1);

    // The next message continues the same conversation.
    fetchMock.mockImplementationOnce(async () =>
      streamResponse([run.started('conv-7'), run.delta('Only billing.'), run.done('conv-7', 't4')]),
    );
    await userEvent.type(screen.getByLabelText('Message'), 'again');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('Only billing.');
    const chats = fetchMock.mock.calls.filter(([u]) => u === '/api/chat') as unknown as [
      string,
      RequestInit,
    ][];
    expect(JSON.parse(chats[1][1].body as string).threadId).toBe('conv-7');
  });

  it('shows "Conversation not found" for an unknown or deleted id', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) =>
          url.startsWith('/api/conversations?')
            ? jsonResponse(page())
            : jsonResponse({ error: 'not_found' }, 404),
        ),
      ),
    );
    renderChat('/chat/gone');
    expect(await screen.findByText('Conversation not found.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Start a new conversation' }));
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
    expect(screen.queryByText('Conversation not found.')).not.toBeInTheDocument();
  });

  it('moves to /chat/{id} after the first answer', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/api/chat')
        return streamResponse([run.delta('Hello.'), run.done('conv-new', 't1')]);
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat('/chat');
    await userEvent.type(screen.getByLabelText('Message'), 'hi');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('Hello.');

    // The conversation is the thread the client named for the run, which the server keeps (agui-protocol-only).
    await waitFor(() =>
      expect(screen.getByTestId('location').textContent).toMatch(/^\/chat\/c_[0-9a-f]{32}$/),
    );
    const id = screen.getByTestId('location').textContent!.slice('/chat/'.length);
    // Still the live turn: the page did not reload the conversation it already shows.
    expect(fetchMock.mock.calls.map((c) => c[0])).not.toContain(`/api/conversations/${id}`);
    expect(screen.getAllByTestId('assistant-turn')).toHaveLength(1);
  });

  it('starting a new conversation is not undone by the cached one', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) => {
          if (url.startsWith('/api/conversations?')) return jsonResponse(page('conv-7'));
          if (url === '/api/conversations/conv-7') return jsonResponse(detail);
          return jsonResponse({}, 404);
        }),
      ),
    );

    // Opening the conversation first is what primes the query cache — the cache is the trap.
    renderChat('/chat/conv-7');
    expect(await screen.findAllByTestId('assistant-turn')).toHaveLength(2);

    // The one "New conversation", in the chat's own header (decision 5y).
    await userEvent.click(screen.getByRole('button', { name: 'New conversation' }));

    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
    expect(screen.queryAllByTestId('assistant-turn')).toHaveLength(0);
    expect(screen.queryByText('What if a fee schedule is missing?')).not.toBeInTheDocument();
    // The cached conversation must not creep back once the route has settled.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
    expect(screen.queryAllByTestId('assistant-turn')).toHaveLength(0);
  });

  it('switching persona clears the chat and leaves the conversation', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string, init?: RequestInit) => {
          void init;
          if (url === '/api/conversations/adam-1')
            return jsonResponse({ ...detail, conversationId: 'adam-1' });
          return jsonResponse({}, 404);
        }),
      ),
    );
    function SwitchPersona() {
      const { setSession } = useAuth();
      return (
        <button type="button" onClick={() => setSession(makeSession('TENANT_ADMIN'))}>
          switch
        </button>
      );
    }

    renderChat('/chat/adam-1', <SwitchPersona />);
    expect(await screen.findAllByTestId('assistant-turn')).toHaveLength(2);

    await userEvent.click(screen.getByRole('button', { name: 'switch' }));
    await waitFor(() => expect(screen.queryAllByTestId('assistant-turn')).toHaveLength(0));
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/chat$/);
  });
  it('a reopened turn shows the reasoning the conversation kept, collapsed, and none when it had none', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) => {
          if (url.startsWith('/api/conversations?')) return jsonResponse(page('conv-7'));
          if (url === '/api/conversations/conv-7') return jsonResponse(detail);
          return jsonResponse({}, 404);
        }),
      ),
    );

    renderChat('/chat/conv-7');
    const turns = await screen.findAllByTestId('assistant-turn');

    // The turn's reasoning comes with the conversation itself, whatever plugins are in use.
    const block = await within(turns[1]).findByTestId('reasoning');
    const summary = within(block).getByRole('button');
    expect(summary).toHaveAttribute('aria-expanded', 'false');
    expect(summary).toHaveTextContent('Thought for 0.2 s');

    await userEvent.click(summary);
    expect(block).toHaveTextContent('The failure code says FS-REQUIRED');

    // t1's model did not reason, so there is nothing to show for it.
    expect(within(turns[0]).queryByTestId('reasoning')).not.toBeInTheDocument();
  });
});
