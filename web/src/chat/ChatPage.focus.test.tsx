import { EventType } from '@ag-ui/core';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders, run, sse, streamResponse } from '../test/render';
import { agentFetch } from '../test/agentFetch';
import { ChatPage } from './ChatPage';

const state = (accountId: string | null) =>
  sse(EventType.STATE_SNAPSHOT, { snapshot: { focus: accountId ? { accountId } : null } });

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
});
