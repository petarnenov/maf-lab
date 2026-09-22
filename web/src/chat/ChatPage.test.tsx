import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { fixtureTrace } from '../monitor/fixtures';
import { jsonResponse, renderWithProviders, run, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

const emptyHistory = { conversations: [], nextCursor: null };

describe('ChatPage', () => {
  it('streams a turn with a tool card, sources and feedback buttons', async () => {
    const fetchMock = vi.fn(async (url: string) =>
      url.startsWith('/api/conversations')
        ? jsonResponse(emptyHistory)
        : streamResponse([
            ...run.toolCall('c1', 'search_documents', 'sourceTypes=docs'),
            run.toolResult('c1', '2 snippets', 'search_documents', 2),
            run.sources([
              {
                docId: 'd1',
                sectionPath: 'Fees > Missing',
                sourcePath: 'shared/docs/fees.md',
                snippet: 's',
              },
            ]),
            run.delta('Open a ticket.'),
            run.done('conv-1', 't1'),
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
    // A turn is a run of the agent: a thread, a run and the message.
    const body = JSON.parse(init.body as string);
    expect(body.threadId).toBeNull();
    expect(body.runId).toMatch(/^r-/);
    expect(body.messages).toEqual([
      {
        id: expect.stringMatching(/^u-/),
        role: 'user',
        content: 'What if a fee schedule is missing?',
      },
    ]);
  });

  it('shows the live trace in the monitor next to the chat', async () => {
    const trace = fixtureTrace.slice(0, 3);
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url === '/api/chat') {
          return streamResponse([
            ...trace.map((e) => run.trace(e)),
            run.delta('Answer.'),
            run.done('conv-1', 't1'),
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

  it('the button closes the monitor and opens it again', async () => {
    const trace = fixtureTrace.slice(0, 3);
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url === '/api/chat') {
          return streamResponse([
            ...trace.map((e) => run.trace(e)),
            run.delta('Answer.'),
            run.done('conv-1', 't1'),
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
    await userEvent.type(screen.getByLabelText('Message'), 'q');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    const turn = await screen.findByTestId('assistant-turn');

    // The turn the monitor is showing says so, and pressing it closes the panel…
    await userEvent.click(within(turn).getByRole('button', { name: 'Showing behind the scenes' }));
    expect(screen.queryByRole('region', { name: 'Behind the scenes' })).not.toBeInTheDocument();
    expect(turn).toHaveAttribute('data-selected', 'false');

    // …and pressing it again opens it on the same turn. This is the direction that used to do nothing.
    await userEvent.click(within(turn).getByRole('button', { name: 'Behind the scenes' }));
    const monitor = screen.getByRole('region', { name: 'Behind the scenes' });
    expect(
      await within(monitor).findByText('Intent: Procedural (forced retrieval)'),
    ).toBeInTheDocument();
    expect(turn).toHaveAttribute('data-selected', 'true');
  });

  it('clicking the bubble shows its trace but never closes the monitor', async () => {
    const trace = fixtureTrace.slice(0, 3);
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url === '/api/chat') {
          return streamResponse([
            ...trace.map((e) => run.trace(e)),
            run.delta('Answer.'),
            run.done('conv-1', 't1'),
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
    await userEvent.type(screen.getByLabelText('Message'), 'q');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    const turn = await screen.findByTestId('assistant-turn');

    await userEvent.click(turn);
    expect(screen.getByRole('region', { name: 'Behind the scenes' })).toBeInTheDocument();
    await userEvent.click(turn);
    expect(screen.getByRole('region', { name: 'Behind the scenes' })).toBeInTheDocument();
  });

  it('selecting an earlier turn loads its stored trace', async () => {
    let chatCalls = 0;
    const storedFirst = fixtureTrace;
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/api/chat') {
        chatCalls += 1;
        const turnId = `t${chatCalls}`;
        return streamResponse([
          run.trace({ ...fixtureTrace[0], title: `Live start ${turnId}` }),
          run.delta(`Answer ${chatCalls}`),
          run.done('conv-1', turnId),
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
          ? streamResponse([run.delta('First answer.'), run.done('conv-1', 't1')])
          : streamResponse([
              ...fixtureTrace.map((e) => run.trace(e)),
              run.delta(answer),
              run.done('conv-1', 't2'),
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
  it('shows the reasoning while the model thinks, then collapses it when the answer starts', async () => {
    const stream = controlled();
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url.startsWith('/api/conversations')
          ? jsonResponse(emptyHistory)
          : url.startsWith('/api/turns')
            ? jsonResponse({ events: [] })
            : stream.response,
      ),
    );

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    stream.push(run.started(), ...run.reasoning('The run failed ', 'on a missing schedule.'));
    const block = await screen.findByTestId('reasoning');
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'true');
    expect(block).toHaveTextContent('The run failed on a missing schedule.');
    expect(within(block).getByRole('button')).toHaveTextContent('Thinking…');

    stream.push(...run.text('Assign the schedule.'));
    expect(await screen.findByText('Assign the schedule.')).toBeInTheDocument();
    // The answer arrived, so the block closed itself and says how long the model thought.
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'false');
    expect(within(block).getByRole('button')).toHaveTextContent(/Thought for \d+\.\d s/);
    expect(block).not.toHaveTextContent('on a missing schedule.');

    stream.push(run.done('conv-1', 't1'));
  });

  it('keeps the reasoning open once a person opens it, and shows none for a turn without any', async () => {
    const stream = controlled();
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url.startsWith('/api/conversations')
          ? jsonResponse(emptyHistory)
          : url.startsWith('/api/turns')
            ? jsonResponse({ events: [] })
            : stream.response,
      ),
    );

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    stream.push(run.started(), ...run.reasoning('Thinking it over.'), run.delta('Assign it.'));
    const block = await screen.findByTestId('reasoning');
    await screen.findByText('Assign it.');
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(within(block).getByRole('button'));
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'true');

    // More of the answer must not take the block back.
    stream.push(run.delta(' Then re-run.'));
    expect(await screen.findByText('Assign it. Then re-run.')).toBeInTheDocument();
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'true');

    stream.push(run.done('conv-1', 't1'));
  });

  it('shows no reasoning block for a turn whose model did not reason', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url.startsWith('/api/conversations')
          ? jsonResponse(emptyHistory)
          : url.startsWith('/api/turns')
            ? jsonResponse({ events: [] })
            : streamResponse([run.started(), ...run.text('Straight to it.'), run.done()]),
      ),
    );

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'hello');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    await screen.findByText('Straight to it.');
    expect(screen.queryByTestId('reasoning')).not.toBeInTheDocument();
  });
});

/** A response whose frames the test pushes one at a time, as the server would. */
function controlled() {
  const encoder = new TextEncoder();
  let controller!: ReadableStreamDefaultController<Uint8Array>;
  const response = new Response(
    new ReadableStream<Uint8Array>({
      start(next) {
        controller = next;
      },
    }),
    { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
  );
  return {
    response,
    push: (...frames: string[]) => frames.forEach((f) => controller.enqueue(encoder.encode(f))),
    close: () => controller.close(),
  };
}
