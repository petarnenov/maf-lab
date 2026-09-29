import { EventType } from '@ag-ui/core';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders, run, sse, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

const state = (accountId: string | null) =>
  sse(EventType.STATE_SNAPSHOT, { snapshot: { focus: accountId ? { accountId } : null } });

const accountsCard = sse(EventType.ACTIVITY_SNAPSHOT, {
  messageId: 'card-c1',
  activityType: 'maf-lab/accounts',
  content: {
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
  },
});

/** Answers each run with the given frames, in turn, and keeps the request bodies. */
function stubRuns(...runs: string[][]) {
  const bodies: Record<string, unknown>[] = [];
  let n = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      if (url !== '/api/chat') return jsonResponse({}, 404);
      bodies.push(JSON.parse(init!.body as string));
      return streamResponse(runs[Math.min(n++, runs.length - 1)]);
    }),
  );
  return bodies;
}

async function ask(text: string) {
  await userEvent.type(screen.getByLabelText('Message'), text);
  await userEvent.click(screen.getByRole('button', { name: 'Send' }));
}

describe('ChatPage focus', () => {
  it('shows the account in focus from the run state, and sends it back with the next message', async () => {
    const bodies = stubRuns(
      [run.started(), state(null), state('A-1043'), run.delta('Done.'), run.done()],
      [run.started(), state('A-1043'), run.delta('Again.'), run.done()],
    );
    renderWithProviders(<ChatPage />);

    await ask('Rebalance A-1043');
    expect(await screen.findByTestId('focus-chip')).toHaveTextContent('Focus: A-1043');
    expect(bodies[0].state).toEqual({ focus: null });

    await ask('and its AUM?');
    await screen.findByText('Again.');
    expect(bodies[1].state).toEqual({ focus: { accountId: 'A-1043' } });
  });

  it('puts an account from an accounts card in focus, to go with the next message', async () => {
    const bodies = stubRuns(
      [run.started(), state(null), accountsCard, run.delta('Two accounts.'), run.done()],
      [run.started(), state('A-1044'), run.delta('It holds…'), run.done()],
    );
    renderWithProviders(<ChatPage />);
    await ask('Which accounts do I have?');
    const card = await screen.findByTestId('data-card');

    await userEvent.click(within(card).getByRole('button', { name: 'Focus A-1044' }));

    expect(screen.getByTestId('focus-chip')).toHaveTextContent('A-1044');
    // Choosing started nothing: still one run.
    expect(bodies).toHaveLength(1);
    await ask('What does it hold?');
    await screen.findByText('It holds…');
    expect(bodies[1].state).toEqual({ focus: { accountId: 'A-1044' } });
  });

  it('clears the focus with ✕ and says so with the next message', async () => {
    const bodies = stubRuns(
      [run.started(), state('A-1043'), run.delta('Done.'), run.done()],
      [run.started(), state(null), run.delta('Cleared.'), run.done()],
    );
    renderWithProviders(<ChatPage />);
    await ask('Rebalance A-1043');
    await screen.findByTestId('focus-chip');

    await userEvent.click(screen.getByRole('button', { name: 'Clear focus' }));

    expect(screen.queryByTestId('focus-chip')).not.toBeInTheDocument();
    await ask('hello');
    await screen.findByText('Cleared.');
    expect(bodies[1].state).toEqual({ focus: null });
  });

  it('follows the server when it refuses the account the client sent', async () => {
    stubRuns(
      [run.started(), state(null), accountsCard, run.delta('Two accounts.'), run.done()],
      [run.started(), state('A-1043'), run.delta('Refused.'), run.done()],
    );
    renderWithProviders(<ChatPage />);
    await ask('Which accounts do I have?');
    await userEvent.click(
      within(await screen.findByTestId('data-card')).getByRole('button', { name: 'Focus A-1044' }),
    );

    await ask('What does it hold?');
    await screen.findByText('Refused.');

    expect(screen.getByTestId('focus-chip')).toHaveTextContent('A-1043');
  });
});
