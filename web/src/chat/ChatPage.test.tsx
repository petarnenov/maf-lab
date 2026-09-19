import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { fixtureTrace } from '../monitor/fixtures';
import { jsonResponse, renderWithProviders, sse, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

describe('ChatPage', () => {
  it('streams a turn with a tool card, sources and feedback buttons', async () => {
    const fetchMock = vi.fn(async () =>
      streamResponse([
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

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/chat');
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
    expect(within(monitor).getAllByRole('listitem')).toHaveLength(3);
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

  it('asks for a persona when signed out', () => {
    renderWithProviders(<ChatPage />, { session: null });
    expect(screen.getByText(/Pick a dev persona/)).toBeInTheDocument();
  });
});
