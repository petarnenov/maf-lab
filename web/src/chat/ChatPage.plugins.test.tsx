import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { definePlugin, type ChatContext, type PluginRunObserver } from '../plugins/api';
import { agentFetch } from '../test/agentFetch';
import { renderChat } from '../test/chat';
import { jsonResponse, run, streamResponse } from '../test/render';

/**
 * What the chat gives the plugins in use about its runs and turns (introduce-plugins 5.3): each run's official AG-UI
 * events to their run observers, and a turn's bubble shown as a pane says it was at an earlier step.
 */

const emptyHistory = { conversations: [], nextCursor: null };

function answering(...frames: string[]) {
  vi.stubGlobal(
    'fetch',
    agentFetch(
      vi.fn(async (url: string) =>
        url.startsWith('/api/conversations')
          ? jsonResponse(emptyHistory)
          : url === '/api/chat'
            ? streamResponse(frames)
            : jsonResponse({}, 404),
      ),
    ),
  );
}

async function ask(question = 'What if a fee schedule is missing?') {
  await userEvent.type(screen.getByLabelText('Message'), question);
  await userEvent.click(screen.getByRole('button', { name: 'Send' }));
}

describe('ChatPage and the plugins in use', () => {
  it("hands each run's official events to the run observers, from its start to its end", async () => {
    const seen: string[] = [];
    let started: Parameters<NonNullable<PluginRunObserver['onRunStart']>>[0] | undefined;
    const observer: PluginRunObserver = {
      onRunStart: (r) => {
        started = r;
        seen.push('start');
      },
      onEvent: (runId, event) => seen.push(`${runId === started?.runId ? '' : '?'}${event.type}`),
      onRunEnd: (runId, outcome) =>
        seen.push(`${runId === started?.runId ? '' : '?'}end:${outcome}`),
    };
    answering(run.started(), ...run.text('Assign it.'), run.done('conv-1', 't1'));

    renderChat([definePlugin({ name: 'watcher', runObservers: [observer] })]);
    await ask();
    await screen.findByText('Assign it.');

    await waitFor(() => expect(seen.at(-1)).toBe('end:finished'));
    expect(seen).toEqual([
      'start',
      'RUN_STARTED',
      'TEXT_MESSAGE_START',
      'TEXT_MESSAGE_CONTENT',
      'TEXT_MESSAGE_END',
      'RUN_FINISHED',
      'end:finished',
    ]);
    expect(started?.turnKey).toMatch(/^a-/);
    expect(started?.conversationId).toMatch(/^c_/);
  });

  it('keeps the chat going when a run observer throws', async () => {
    const failing: PluginRunObserver = {
      onEvent: () => {
        throw new Error('plugin bug');
      },
    };
    answering(run.started(), ...run.text('Still here.'), run.done());

    renderChat([definePlugin({ name: 'broken', runObservers: [failing] })]);
    await ask();

    expect(await screen.findByText('Still here.')).toBeInTheDocument();
  });

  it("shows a turn as a pane says it was, read-only, until the pane's way back is taken", async () => {
    const onExit = vi.fn();
    function Rewinder({ context }: { context: ChatContext }) {
      const { turn, setTurnView } = context;
      const key = turn && !turn.streaming ? turn.key : undefined;
      useEffect(() => {
        if (!key || !setTurnView) return;
        setTurnView(key, {
          text: 'Half an answer',
          toolCalls: [],
          sources: [],
          cards: [],
          label: 'step 2 of 9',
          note: 'answer text not recorded for this turn',
          onExit,
        });
        return () => setTurnView(key, null);
      }, [key, setTurnView]);
      return <p>rewinder</p>;
    }
    const rewinder = definePlugin({
      name: 'rewinder',
      chatPanes: [
        { id: 'rw', label: 'Rewind', render: (context) => <Rewinder context={context} /> },
      ],
    });
    answering(run.started(), ...run.text('The whole answer.'), run.done());

    renderChat([rewinder]);
    await ask();

    const turn = await screen.findByTestId('assistant-turn');
    const banner = await within(turn).findByTestId('rewind-banner');
    expect(banner).toHaveTextContent('Viewing step 2 of 9');
    expect(banner).toHaveTextContent('answer text not recorded for this turn');
    expect(within(turn).getByText('Half an answer')).toBeInTheDocument();
    expect(within(turn).queryByText('The whole answer.')).not.toBeInTheDocument();
    // The bubble's own button names the pane it opens.
    expect(within(turn).getByRole('button', { name: 'Showing rewind' })).toBeInTheDocument();

    await userEvent.click(within(banner).getByRole('button', { name: 'Return to now' }));
    expect(onExit).toHaveBeenCalledTimes(1);

    // Closing the side pane shows the turn as it is.
    await userEvent.click(within(turn).getByRole('button', { name: 'Showing rewind' }));
    expect(within(turn).queryByTestId('rewind-banner')).not.toBeInTheDocument();
    expect(within(turn).getByText('The whole answer.')).toBeInTheDocument();
  });
});
