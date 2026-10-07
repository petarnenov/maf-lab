import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { agentFetch, jsonResponse, renderChat, run, streamResponse } from '@maf/testing';
import portfolio from './index';

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

/** A response whose body the test releases in two parts, so it can look at the screen in between. */
function pausedStream(first: string[], rest: string[]) {
  const encoder = new TextEncoder();
  let release!: () => void;
  const body = new ReadableStream<Uint8Array>({
    async start(controller) {
      for (const c of first) controller.enqueue(encoder.encode(c));
      await new Promise<void>((r) => (release = r));
      for (const c of rest) controller.enqueue(encoder.encode(c));
      controller.close();
    },
  });
  return {
    response: new Response(body, { headers: { 'Content-Type': 'text/event-stream' } }),
    release: () => release(),
  };
}

const state = (accountId: string | null) => run.state({ focus: accountId ? { accountId } : null });

const accountsCard = run.card('card-c1', 'maf-lab/accounts', {
  count: 2,
  accounts: [
    {
      accountId: 'A-1043',
      name: 'Calder',
      householdId: 'HH-C',
      modelPortfolio: 'M',
      currency: 'USD',
    },
    {
      accountId: 'A-1044',
      name: 'Whitfield',
      householdId: 'HH-W',
      modelPortfolio: 'M',
      currency: 'USD',
    },
  ],
});

/** Answers each run with the given frames, in turn, and keeps the request bodies. */
function stubRuns(...runs: string[][]) {
  const bodies: Record<string, unknown>[] = [];
  let n = 0;
  vi.stubGlobal(
    'fetch',
    agentFetch(
      vi.fn(async (url: string, init?: RequestInit) => {
        if (url !== '/api/chat') return jsonResponse({}, 404);
        bodies.push(JSON.parse(init!.body as string));
        return streamResponse(runs[Math.min(n++, runs.length - 1)]);
      }),
    ),
  );
  return bodies;
}

async function ask(text: string) {
  await userEvent.type(screen.getByLabelText('Message'), text);
  await userEvent.click(screen.getByRole('button', { name: 'Send' }));
}

describe('portfolio cards in the chat', () => {
  it('shows a card the moment it arrives, before any answer text', async () => {
    const stream = pausedStream(
      [
        run.started(),
        ...run.toolCall('c1', 'get_household_portfolio', 'accountId=A-1043'),
        run.toolResult('c1', 'A-1043: 1 holdings', 'get_household_portfolio'),
        card,
      ],
      [run.delta('Не е необходимо ребалансиране.'), run.done('conv-1', 't1')],
    );
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) =>
          url === '/api/chat' ? stream.response : jsonResponse({}, 404),
        ),
      ),
    );
    renderChat([portfolio]);

    await userEvent.type(screen.getByLabelText('Message'), 'Препоръчай ребалансиране за A-1043');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const turn = await screen.findByTestId('assistant-turn');
    const shown = await within(turn).findByTestId('data-card');
    expect(within(shown).getByText('Продажба')).toBeInTheDocument();
    expect(within(turn).queryByText('Не е необходимо ребалансиране.')).not.toBeInTheDocument();

    stream.release();
    expect(await within(turn).findByText('Не е необходимо ребалансиране.')).toBeInTheDocument();
    expect(within(turn).getAllByTestId('data-card')).toHaveLength(1);
  });

  it('shows a restored turn’s card', async () => {
    const detail = {
      conversationId: 'conv-9',
      title: 'Rebalance',
      createdAt: '2026-09-29T09:00:00Z',
      lastActivityAt: '2026-09-29T09:00:00Z',
      turns: [
        {
          turnId: 't1',
          question: 'Rebalance A-1043',
          answer: 'No rebalance needed.',
          createdAt: '2026-09-29T09:00:00Z',
          toolCalls: [],
          sources: [],
          feedbackKinds: [],
          activities: [{ messageId: 'card-c1', activityType: 'maf-lab/holdings', content }],
        },
      ],
    };
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) => {
          if (url.startsWith('/api/conversations?'))
            return jsonResponse({ conversations: [], nextCursor: null });
          if (url === '/api/conversations/conv-9') return jsonResponse(detail);
          return jsonResponse({}, 404);
        }),
      ),
    );
    renderChat([portfolio], { route: '/chat/conv-9' });

    const shown = await screen.findByTestId('data-card');
    expect(within(shown).getByText('Sell')).toBeInTheDocument();
  });

  it('puts an account from an accounts card in focus, to go with the next message', async () => {
    const bodies = stubRuns(
      [run.started(), state(null), accountsCard, run.delta('Two accounts.'), run.done()],
      [run.started(), state('A-1044'), run.delta('It holds…'), run.done()],
    );
    renderChat([portfolio]);
    await ask('Which accounts do I have?');
    const shown = await screen.findByTestId('data-card');

    await userEvent.click(within(shown).getByRole('button', { name: 'Focus A-1044' }));

    expect(screen.getByTestId('focus-chip')).toHaveTextContent('A-1044');
    // Choosing started nothing: still one run.
    expect(bodies).toHaveLength(1);
    await ask('What does it hold?');
    await screen.findByText('It holds…');
    expect(bodies[1].state).toEqual({ focus: { accountId: 'A-1044' } });
  });

  it('follows the server when it refuses the account the client sent', async () => {
    stubRuns(
      [run.started(), state(null), accountsCard, run.delta('Two accounts.'), run.done()],
      [run.started(), state('A-1043'), run.delta('Refused.'), run.done()],
    );
    renderChat([portfolio]);
    await ask('Which accounts do I have?');
    await userEvent.click(
      within(await screen.findByTestId('data-card')).getByRole('button', { name: 'Focus A-1044' }),
    );

    await ask('What does it hold?');
    await screen.findByText('Refused.');

    expect(screen.getByTestId('focus-chip')).toHaveTextContent('A-1043');
  });
});
