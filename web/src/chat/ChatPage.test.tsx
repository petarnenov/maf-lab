import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { fixtureTrace } from '../monitor/fixtures';
import { jsonResponse, renderWithProviders, sse, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

const emptyHistory = { conversations: [], nextCursor: null };

describe('ChatPage', () => {
  it('streams a turn with a tool card, sources and feedback buttons', async () => {
    const fetchMock = vi.fn(async (url: string) =>
      url.startsWith('/api/conversations')
        ? jsonResponse(emptyHistory)
        : streamResponse([
            sse('tool_call_started', {
              callId: 'c1',
              toolName: 'search_documents',
              argumentSummary: 'query="fee"',
            }),
            sse('tool_call_finished', {
              callId: 'c1',
              toolName: 'search_documents',
              resultSummary: '2 snippets',
              sourceCount: 2,
              isError: false,
            }),
            sse('sources', {
              sources: [
                {
                  docId: 'd1',
                  sectionPath: 'Fees > Missing',
                  sourcePath: 'shared/docs/fees.md',
                  snippet: 's',
                },
              ],
            }),
            sse('text_delta', { text: 'Open a ticket.' }),
            sse('done', { conversationId: 'conv-1', turnId: 't1' }),
          ]),
    );
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const turn = await screen.findByTestId('assistant-turn');
    expect(await within(turn).findByText('Open a ticket.')).toBeInTheDocument();
    expect(within(turn).getByTestId('tool-call-card')).toHaveTextContent('2 sources');
    expect(within(turn).getByText('Sources (1)')).toBeInTheDocument();
    expect(within(turn).getByRole('button', { name: 'Wrong document' })).toBeInTheDocument();

    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/chat') as unknown as [
      string,
      RequestInit,
    ];
    expect(JSON.parse(init.body as string)).toEqual({
      message: 'What if a fee schedule is missing?',
    });
  });

  it('shows the live trace in the monitor next to the chat', async () => {
    const trace = fixtureTrace.slice(0, 3);
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url === '/api/chat') {
          return streamResponse([
            ...trace.map((e) => sse('trace', e)),
            sse('text_delta', { text: 'Answer.' }),
            sse('done', { conversationId: 'conv-1', turnId: 't1' }),
          ]);
        }
        if (url.startsWith('/api/conversations')) return jsonResponse(emptyHistory);
        return jsonResponse({
          turnId: 't1',
          conversationId: 'conv-1',
          createdAt: '',
          events: trace,
        });
      }),
    );

    renderWithProviders(<ChatPage />);
    expect(screen.getByRole('region', { name: 'Behind the scenes' })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Message'), 'q');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const monitor = screen.getByRole('region', { name: 'Behind the scenes' });
    expect(
      await within(monitor).findByText('Intent: Procedural (forced retrieval)'),
    ).toBeInTheDocument();
    const timeline = within(monitor).getByRole('list', { name: 'Timeline' });
    expect(within(timeline).getAllByRole('listitem')).toHaveLength(3);
  });

  it('selecting an earlier turn loads its stored trace', async () => {
    let chatCalls = 0;
    const storedFirst = fixtureTrace;
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/api/chat') {
        chatCalls += 1;
        const turnId = `t${chatCalls}`;
        return streamResponse([
          sse('trace', { ...fixtureTrace[0], title: `Live start ${turnId}` }),
          sse('text_delta', { text: `Answer ${chatCalls}` }),
          sse('done', { conversationId: 'conv-1', turnId }),
        ]);
      }
      if (url === '/api/turns/t1/trace') {
        return jsonResponse({
          turnId: 't1',
          conversationId: 'conv-1',
          createdAt: '',
          events: storedFirst,
        });
      }
      if (url === '/api/turns/t2/trace') {
        return jsonResponse({
          turnId: 't2',
          conversationId: 'conv-1',
          createdAt: '',
          events: [fixtureTrace[0]],
        });
      }
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<ChatPage />);
    for (const q of ['first', 'second']) {
      await userEvent.type(screen.getByLabelText('Message'), q);
      await userEvent.click(screen.getByRole('button', { name: 'Send' }));
      await screen.findByText(`Answer ${q === 'first' ? 1 : 2}`);
    }

    const [firstTurn] = screen.getAllByTestId('assistant-turn');
    await userEvent.click(within(firstTurn).getByRole('button', { name: 'Behind the scenes' }));

    const monitor = screen.getByRole('region', { name: 'Behind the scenes' });
    expect(await within(monitor).findByText(`${storedFirst.length} events`)).toBeInTheDocument();
    expect(fetchMock.mock.calls.map((c) => c[0])).toContain('/api/turns/t1/trace');
    expect(firstTurn).toHaveAttribute('data-selected', 'true');
  });

  it('time travel rewinds the selected answer only, and chat typing never scrubs', async () => {
    let chatCalls = 0;
    const answer = 'Assign the missing fee schedule and re-run the billing run.';
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/api/chat') {
        chatCalls += 1;
        return chatCalls === 1
          ? streamResponse([
              sse('text_delta', { text: 'First answer.' }),
              sse('done', { conversationId: 'conv-1', turnId: 't1' }),
            ])
          : streamResponse([
              ...fixtureTrace.map((e) => sse('trace', e)),
              sse('text_delta', { text: answer }),
              sse('done', { conversationId: 'conv-1', turnId: 't2' }),
            ]);
      }
      if (url === '/api/turns/t2/trace') {
        return jsonResponse({
          turnId: 't2',
          conversationId: 'conv-1',
          createdAt: '',
          events: fixtureTrace,
        });
      }
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<ChatPage />);
    for (const q of ['first', 'second']) {
      await userEvent.type(screen.getByLabelText('Message'), q);
      await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    }
    await screen.findByText(answer);
    const [firstTurn, secondTurn] = screen.getAllByTestId('assistant-turn');
    expect(within(secondTurn).queryByTestId('rewind-banner')).not.toBeInTheDocument();

    // Typing arrows in the chat box must not move the cursor.
    await userEvent.type(screen.getByLabelText('Message'), '{ArrowLeft}{Home}');
    expect(screen.getByTestId('tt-step')).toHaveTextContent(
      `step ${fixtureTrace.length} / ${fixtureTrace.length}`,
    );

    // Move onto the first answer chunk.
    const firstChunk = fixtureTrace.findIndex((e) => e.kind === 'answer.delta') + 1;
    screen.getByRole('region', { name: 'Behind the scenes' }).focus();
    await userEvent.keyboard('{Home}');
    for (let i = 0; i < firstChunk; i++) await userEvent.keyboard('{ArrowRight}');

    const banner = within(secondTurn).getByTestId('rewind-banner');
    expect(banner).toHaveTextContent(`Viewing step ${firstChunk} of ${fixtureTrace.length}`);
    expect(within(secondTurn).getByText('Assign the missing fee schedule')).toBeInTheDocument();
    expect(within(secondTurn).queryByText(answer)).not.toBeInTheDocument();
    expect(within(secondTurn).queryByText(/Sources \(/)).not.toBeInTheDocument();
    expect(within(firstTurn).getByText('First answer.')).toBeInTheDocument();
    expect(within(firstTurn).queryByTestId('rewind-banner')).not.toBeInTheDocument();

    // Between the tool call and its result the card is running.
    await userEvent.keyboard('{Home}');
    const callStep = fixtureTrace.findIndex((e) => e.kind === 'tool.call') + 1;
    for (let i = 0; i < callStep; i++) await userEvent.keyboard('{ArrowRight}');
    expect(within(secondTurn).getByTestId('tool-call-card')).toHaveAttribute(
      'data-state',
      'running',
    );

    await userEvent.click(
      within(banner.ownerDocument.body).getByRole('button', { name: 'Return to now' }),
    );
    expect(within(secondTurn).queryByTestId('rewind-banner')).not.toBeInTheDocument();
    expect(within(secondTurn).getByText(answer)).toBeInTheDocument();
  });

  it('asks for a persona when signed out', () => {
    renderWithProviders(<ChatPage />, { session: null });
    expect(screen.getByText(/Pick a dev persona/)).toBeInTheDocument();
  });
});
