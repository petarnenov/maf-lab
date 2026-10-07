import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { screen, waitFor, within } from '@testing-library/react';
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
import { fixtureTrace } from './fixtures';
import monitorPlugin from './index';
import type { TraceEvent } from './types';

/** A stand-in for a domain plugin that draws the holdings card: what time travel hides and shows again. */
const cardsPlugin = definePlugin({
  name: 'cards',
  cards: { 'maf-lab/holdings': () => createElement('div', { 'data-testid': 'data-card' }) },
});

const emptyHistory = { conversations: [], nextCursor: null };

const content = {
  accountId: 'A-1043',
  accountName: 'Calder Retirement Plan',
  driftTolerancePct: 5,
  outsideTolerance: false,
  rebalanceNeeded: false,
  holdings: [
    {
      assetClass: 'US equity',
      marketValue: 268000,
      targetWeightPct: 20,
      actualWeightPct: 20.6,
      driftPct: 0.6,
      outsideTolerance: false,
      tradeToTarget: -8000,
      tradeSide: 'sell',
      weightAfterPct: 20,
    },
  ],
  totalMarketValue: 1300000,
  currency: 'USD',
  asOf: '2026-09-30',
};

const card = run.card('card-c1', 'maf-lab/holdings', content);

/** A run that ends paused on a fee adjustment for the person to approve. */
const paused = () =>
  `event: RUN_FINISHED\ndata: ${JSON.stringify({
    type: 'RUN_FINISHED',
    threadId: 'conv-1',
    runId: 't1',
    outcome: {
      type: 'interrupt',
      interrupts: [
        {
          id: 'adj_1',
          reason: 'approval_required',
          message: 'Apply a fee adjustment of -200.00 USD to A-1042 (Ridgeline Family Trust)?',
          toolCallId: 'c1',
          expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
          metadata: {
            writeId: 'adj_1',
            toolName: 'propose_fee_adjustment',
            summary: {
              adjustmentId: 'adj_1',
              accountId: 'A-1042',
              accountName: 'Ridgeline Family Trust',
              currentFee: 1200,
              amount: -200,
              resultingFee: 1000,
              currency: 'USD',
              periodStart: '2026-10-01',
              periodEnd: '2026-10-31',
            },
          },
        },
      ],
    },
  })}\n\n`;

// The monitor's pane inside the core's chat: the run's trace beside it, and time travel through the chat's turn view.
describe('ChatPage with the monitor', () => {
  it('shows the live trace in the monitor next to the chat', async () => {
    const trace = fixtureTrace.slice(0, 3);
    vi.stubGlobal(
      'fetch',
      agentFetch(
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
      ),
    );

    renderChat([monitorPlugin]);
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
      agentFetch(
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
      ),
    );

    renderChat([monitorPlugin]);
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
      agentFetch(
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
      ),
    );

    renderChat([monitorPlugin]);
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
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat([monitorPlugin]);
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
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat([monitorPlugin]);
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

  it('never flashes the rewind banner while a turn streams its reasoning', async () => {
    const stream = controlledStreamResponse();
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) =>
          url.startsWith('/api/conversations')
            ? jsonResponse(emptyHistory)
            : url.startsWith('/api/turns')
              ? jsonResponse({ events: [] })
              : stream.response,
        ),
      ),
    );
    // Any render that commits the banner, however briefly, is recorded.
    let flashed = false;
    const observer = new MutationObserver(() => {
      if (document.querySelector('[data-testid="rewind-banner"]')) flashed = true;
    });
    observer.observe(document.body, { childList: true, subtree: true });

    renderChat([monitorPlugin]);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    stream.push(run.started());
    // Each chunk arrives with its trace event, as the server sends them: the trace grows with the reasoning.
    const template = fixtureTrace.find((e) => e.kind === 'reasoning.delta')!;
    let reasoned = '';
    for (const [i, chunk] of ['The run ', 'failed ', 'on a ', 'missing schedule.'].entries()) {
      stream.push(
        run.trace({
          ...template,
          seq: i + 1,
          atMs: i * 100,
          data: { offset: reasoned.length, text: chunk },
        }),
        ...run.reasoning(chunk),
      );
      reasoned += chunk;
      await screen.findByText(reasoned.trim());
    }
    stream.push(...run.text('Assign the schedule.'));
    await screen.findByText('Assign the schedule.');
    stream.push(run.done('conv-1', 't1'));
    await new Promise((r) => setTimeout(r, 0));
    observer.disconnect();

    expect(flashed).toBe(false);
  });

  it('hides the card when time travel goes back before it, and shows it again at its step', async () => {
    const cardEvent: TraceEvent = {
      seq: fixtureTrace.length + 1,
      atMs: 5000,
      kind: 'card',
      title: 'Data card maf-lab/holdings',
      data: { callId: 'c1', messageId: 'card-c1', activityType: 'maf-lab/holdings', content },
      truncated: false,
    };
    const trace = [...fixtureTrace, cardEvent];
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) =>
          url === '/api/chat'
            ? streamResponse([
                ...trace.map((e) => run.trace(e)),
                card,
                run.delta('Done.'),
                run.done(),
              ])
            : jsonResponse({}, 404),
        ),
      ),
    );
    renderChat([monitorPlugin, cardsPlugin]);
    await userEvent.type(screen.getByLabelText('Message'), 'Rebalance A-1043');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    const turn = await screen.findByTestId('assistant-turn');
    await within(turn).findByText('Done.');
    expect(within(turn).getByTestId('data-card')).toBeInTheDocument();
    // The trace arrives from the trace API (agui-protocol-only); time travel moves through it once it is there.
    await waitFor(() =>
      expect(screen.getByTestId('tt-step')).toHaveTextContent(
        `step ${trace.length} / ${trace.length}`,
      ),
    );

    screen.getByRole('region', { name: 'Behind the scenes' }).focus();
    await userEvent.keyboard('{Home}');
    expect(within(turn).queryByTestId('data-card')).not.toBeInTheDocument();

    await userEvent.keyboard('{End}');
    expect(within(turn).getByTestId('data-card')).toBeInTheDocument();
  });

  it('monitor panel stays visible and receives streamed tool events while the answer runs', async () => {
    const resume = controlledStreamResponse();
    let chatCalls = 0;
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.startsWith('/api/conversations')) return jsonResponse(emptyHistory);
      if (url.startsWith('/api/turns')) return jsonResponse({ events: [] });
      chatCalls += 1;
      if (chatCalls === 1) {
        return streamResponse([run.started(), ...run.text('I have put it to you.'), paused()]);
      }
      expect(JSON.parse(init!.body as string).resume).toEqual([
        {
          interruptId: 'adj_1',
          status: 'resolved',
          payload: { approve: true, idempotencyKey: 'adj_1:approve' },
        },
      ]);
      return resume.response;
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderChat([monitorPlugin]);
    await userEvent.type(screen.getByLabelText('Message'), 'credit 200 off A-1042');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByTestId('confirmation-card');

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));

    const monitor = await screen.findByRole('region', { name: 'Behind the scenes' });
    expect(within(monitor).getByText(/live/)).toBeInTheDocument();

    resume.push(
      run.started(),
      run.trace({
        seq: 1,
        atMs: 5,
        kind: 'tool.call',
        title: 'search_documents',
        durationMs: null,
        data: {
          callId: 'tc1',
          tool: 'search_documents',
          arguments: { accountId: 'A-1042' },
        },
        truncated: false,
      }),
      ...run.toolCall('tc1', 'search_documents'),
    );

    expect(await screen.findByTestId('tool-call-card')).toBeInTheDocument();
    expect(await within(monitor).findByTestId('monitor-stats')).toHaveTextContent('1 tool calls');
    expect(within(monitor).getByText(/live/)).toBeInTheDocument();

    resume.push(
      run.toolResult('tc1', 'Found 2 docs', 'search_documents', 2),
      ...run.text('Applied. The fee on A-1042 is now adjusted.'),
      run.done('conv-1', 't2'),
    );
    resume.close();

    expect(await screen.findByText(/Applied\. The fee on A-1042/)).toBeInTheDocument();

    // The answer is a run of its own, and its frames reach the monitor like any other run's — the protocol's own events
    // only: its trace is read from the trace API, not carried on the stream (agui-protocol-only).
    await userEvent.click(within(monitor).getByRole('tab', { name: 'AG-UI' }));
    const frames = within(
      await within(monitor).findByRole('list', { name: 'AG-UI frames' }),
    ).getAllByRole('listitem');
    expect(frames.map((f) => f.getAttribute('data-type'))).toEqual([
      'RUN_STARTED',
      'TOOL_CALL_START',
      'TOOL_CALL_ARGS',
      'TOOL_CALL_END',
      'TOOL_CALL_RESULT',
      'TEXT_MESSAGE_START',
      'TEXT_MESSAGE_CONTENT',
      'TEXT_MESSAGE_END',
      'RUN_FINISHED',
    ]);
  });
});
