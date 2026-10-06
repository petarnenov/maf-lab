import { EventType } from '@ag-ui/core';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import type { ConversationDetail } from '../api/types';
import { jsonResponse, renderWithProviders, run, sse } from '../test/render';
import { agentFetch } from '../test/agentFetch';
import { ChatPage } from './ChatPage';

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

const card = sse(EventType.ACTIVITY_SNAPSHOT, {
  messageId: 'card-c1',
  activityType: 'maf-lab/holdings',
  content,
});

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

describe('ChatPage data cards', () => {
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
    renderWithProviders(<ChatPage />);

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
    const detail: ConversationDetail = {
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
    renderWithProviders(
      <Routes>
        <Route path="chat/:conversationId?" element={<ChatPage />} />
      </Routes>,
      { route: '/chat/conv-9' },
    );

    const shown = await screen.findByTestId('data-card');
    expect(within(shown).getByText('Sell')).toBeInTheDocument();
  });
});
