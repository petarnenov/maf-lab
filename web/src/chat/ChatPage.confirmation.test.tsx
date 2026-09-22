import { EventType } from '@ag-ui/core';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Route, Routes } from 'react-router';
import { jsonResponse, renderWithProviders, run, sse, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

const emptyHistory = { conversations: [], nextCursor: null };

const adjustment = {
  adjustmentId: 'adj_1',
  accountId: 'A-1042',
  accountName: 'Ridgeline Family Trust',
  currentFee: 1200,
  amount: -200,
  resultingFee: 1000,
  currency: 'USD',
  periodStart: '2026-10-01',
  periodEnd: '2026-10-31',
};

/** A run that pauses on a proposal, as the server streams it. */
const paused = () =>
  sse(EventType.RUN_FINISHED, {
    threadId: 'conv-1',
    runId: 'r1',
    result: { turnId: 't1' },
    outcome: {
      type: 'interrupt',
      interrupts: [
        {
          id: 'adj_1',
          message: 'Apply a fee adjustment of -200.00 USD to A-1042 (Ridgeline Family Trust)?',
          toolCallId: 'c1',
          expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
          metadata: { adjustment, state: 'opaque', tool: 'propose_fee_adjustment' },
        },
      ],
    },
  });

/**
 * Proposes an adjustment and waits for the card. `answer` is what the run that answers it will say.
 * The chat bodies are recorded so a test can read what was actually sent.
 */
async function propose(answer: string) {
  const bodies: string[] = [];
  let chatCalls = 0;
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url.startsWith('/api/conversations')) return jsonResponse(emptyHistory);
    // Trace requests are GET (no body); return empty trace so useTurnTrace does not count as a chat call.
    if (url.startsWith('/api/turns')) return jsonResponse({ events: [] });
    bodies.push(init!.body as string);
    chatCalls += 1;
    return chatCalls === 1
      ? streamResponse([run.started(), ...run.text('I have put it to you.'), paused()])
      : streamResponse([run.started(), ...run.text(answer), run.done()]);
  });
  vi.stubGlobal('fetch', fetchMock);

  renderWithProviders(<ChatPage />);
  await userEvent.type(screen.getByLabelText('Message'), 'credit 200 off A-1042');
  await userEvent.click(screen.getByRole('button', { name: 'Send' }));
  await screen.findByTestId('confirmation-card');
  return bodies;
}

function controlledStreamResponse(status = 200) {
  const encoder = new TextEncoder();
  let controller!: ReadableStreamDefaultController<Uint8Array>;
  const response = new Response(
    new ReadableStream<Uint8Array>({
      start(next) {
        controller = next;
      },
    }),
    { status, headers: { 'Content-Type': 'text/event-stream' } },
  );
  return {
    response,
    push(...chunks: string[]) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
    },
    close() {
      controller.close();
    },
  };
}

describe('ChatPage reopened with a write waiting', () => {
  const detail = {
    conversationId: 'conv-7',
    title: 'Fee adjustment',
    turns: [
      {
        turnId: 't1',
        question: 'credit 200 off A-1042',
        answer: 'I have put it to you.',
        createdAt: '2026-09-20T12:00:00Z',
        toolCalls: [],
        sources: [],
        feedbackKinds: [],
        traceAvailable: true,
      },
    ],
  };

  const pending = {
    adjustmentId: 'adj_1',
    adjustment,
    question: 'Apply a fee adjustment of -200.00 USD to A-1042 (Ridgeline Family Trust)?',
    expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
  };

  const reopen = (body: { pending: unknown }) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url.endsWith('/pending')) return jsonResponse(body);
        if (url === '/api/conversations/conv-7') return jsonResponse(detail);
        if (url.startsWith('/api/conversations')) return jsonResponse(emptyHistory);
        return jsonResponse({});
      }),
    );
    renderWithProviders(
      <Routes>
        <Route path="chat/:conversationId?" element={<ChatPage />} />
      </Routes>,
      { route: '/chat/conv-7' },
    );
  };

  it('shows the proposal again', async () => {
    reopen({ pending });

    const card = await screen.findByTestId('confirmation-card');
    expect(within(card).getByText(/A-1042 — Ridgeline Family Trust/)).toBeInTheDocument();
    expect(within(card).getByText('1,000.00 USD')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Approve' })).toBeEnabled();
  });

  it('shows no card when nothing is waiting', async () => {
    reopen({ pending: null });

    expect(await screen.findByText('I have put it to you.')).toBeInTheDocument();
    expect(screen.queryByTestId('confirmation-card')).not.toBeInTheDocument();
  });

  it('does not offer to answer a proposal that has expired', async () => {
    reopen({ pending: { ...pending, expiresAt: new Date(Date.now() - 1000).toISOString() } });

    const card = await screen.findByTestId('confirmation-card');
    expect(card).toHaveAttribute('data-state', 'expired');
    expect(within(card).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(within(card).getByText(/too old to apply/)).toBeInTheDocument();
  });
});

describe('ChatPage with a write waiting', () => {
  it('shows the proposal in the conversation, not over it', async () => {
    await propose('unused');

    const card = screen.getByTestId('confirmation-card');
    expect(within(card).getByText(/A-1042 — Ridgeline Family Trust/)).toBeInTheDocument();
    expect(within(card).getByText('1,000.00 USD')).toBeInTheDocument();
    // The conversation is still there to read, and still usable.
    expect(screen.getByText('I have put it to you.')).toBeInTheDocument();
    expect(screen.getByLabelText('Message')).toBeInTheDocument();
  });

  it('approving resumes the proposal and says what happened', async () => {
    const bodies = await propose('Applied. The fee on A-1042 is now 1,000.00 USD.');

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));

    expect(await screen.findByText(/Applied\. The fee on A-1042/)).toBeInTheDocument();
    expect(JSON.parse(bodies.at(-1)!).resume).toEqual([
      { interruptId: 'adj_1', payload: { approve: true } },
    ]);
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
  });

  it('rejecting resumes it as declined and applies nothing', async () => {
    const bodies = await propose('Nothing was applied. The advisor declined the adjustment.');

    await userEvent.click(screen.getByRole('button', { name: 'Reject' }));

    expect(await screen.findByText(/Nothing was applied/)).toBeInTheDocument();
    expect(JSON.parse(bodies.at(-1)!).resume[0].payload).toEqual({ approve: false });
  });

  it('says so when the proposal is no longer waiting', async () => {
    await propose('That proposal is no longer waiting for an answer.');

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));

    // The run says it, and the card settles into it.
    const card = await screen.findByTestId('confirmation-card');
    expect(card).toHaveAttribute('data-state', 'gone');
    expect(within(card).getByText(/no longer waiting/)).toBeInTheDocument();
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
        { interruptId: 'adj_1', payload: { approve: true } },
      ]);
      return resume.response;
    });
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<ChatPage />);
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

    // The answer is a run of its own, and its frames reach the monitor like any other run's.
    await userEvent.click(within(monitor).getByRole('tab', { name: 'AG-UI' }));
    const frames = within(
      await within(monitor).findByRole('list', { name: 'AG-UI frames' }),
    ).getAllByRole('listitem');
    expect(frames.map((f) => f.getAttribute('data-type'))).toEqual([
      'RUN_STARTED',
      'CUSTOM',
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
