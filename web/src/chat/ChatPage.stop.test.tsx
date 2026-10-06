import { EventType } from '@ag-ui/core';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { agentFetch, type StopBehaviour } from '../test/agentFetch';
import {
  controlledStreamResponse,
  jsonResponse,
  renderWithProviders,
  run,
  sse,
  streamResponse,
} from '../test/render';
import { ChatPage } from './ChatPage';

/** A run's end as `@ag-ui/client` reports an aborted run. */
const aborted = () => sse(EventType.RUN_ERROR, { message: 'Request aborted', code: 'abort' });

/** A run's end as CopilotKit's runtime closes a run it stopped. */
const cancelled = (threadId: string, runId: string) =>
  sse(EventType.RUN_FINISHED, { threadId, runId, outcome: { type: 'cancelled' } });

/** A tool call CopilotKit's runtime closed when it stopped the run. */
const stoppedResult = (toolCallId: string) =>
  sse(EventType.TOOL_CALL_RESULT, {
    toolCallId,
    messageId: `${toolCallId}-result`,
    role: 'tool',
    content: JSON.stringify({
      status: 'stopped',
      reason: 'stop_requested',
      message: 'Run stopped by user',
    }),
  });

/**
 * The chat page over a runtime whose runs stay open until the test closes them, or until a stop ends them as
 * CopilotKit's runtime does (`stop`). Returns the streams, the bodies the runs were started with, and the stop
 * requests the runtime received.
 */
function chat({ stop = 'abort' as StopBehaviour } = {}) {
  const streams: ReturnType<typeof controlledStreamResponse>[] = [];
  const bodies: { threadId: string; runId: string }[] = [];
  const handler = vi.fn(async (url: string, init?: RequestInit) => {
    if (url.startsWith('/api/turns')) return jsonResponse({ events: [] });
    if (url === '/api/chat') {
      bodies.push(JSON.parse(String(init?.body)) as { threadId: string; runId: string });
      const stream = controlledStreamResponse();
      streams.push(stream);
      return stream.response;
    }
    return streamResponse([]);
  });
  const runtime = agentFetch(handler, { stop });
  const fetchMock = vi.fn((url: string, init?: RequestInit) => runtime(url, init));
  vi.stubGlobal('fetch', fetchMock);
  renderWithProviders(<ChatPage />);
  const stops = () =>
    fetchMock.mock.calls
      .filter(([url]) => /\/copilotkit\/agent\/chat\/stop\//.test(url))
      .map(([url, init]) => ({
        url,
        body: JSON.parse(String(init?.body ?? '{}')) as { runId?: string },
      }));
  return { streams, bodies, stops };
}

async function ask(text = 'why did the fee go up?') {
  await userEvent.type(screen.getByLabelText('Message'), text);
  await userEvent.click(screen.getByRole('button', { name: 'Send' }));
}

const lastTurn = () => screen.getAllByTestId('assistant-turn').at(-1)!;

describe('ChatPage: Esc stops the answer in progress', () => {
  it('asks CopilotKit to stop the run, and shows it stopped once the run says so', async () => {
    const { streams, bodies, stops } = chat();
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(run.delta('The fee schedule')));
    expect(await screen.findByText('The fee schedule')).toBeInTheDocument();

    await userEvent.keyboard('{Escape}');

    expect(await within(lastTurn()).findByTestId('turn-stopped')).toHaveTextContent('Stopped.');
    expect(stops()).toEqual([
      {
        url: expect.stringContaining(`/stop/${encodeURIComponent(bodies[0].threadId)}`),
        body: { runId: bodies[0].runId },
      },
    ]);
    expect(within(lastTurn()).getByText('The fee schedule')).toBeInTheDocument();
    expect(screen.queryByTestId('run-progress')).not.toBeInTheDocument();
    expect(screen.queryByTestId('turn-error')).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Message'), 'next');
    expect(screen.getByRole('button', { name: 'Send' })).toBeEnabled();
  });

  it('a cancelled outcome is a stop too', async () => {
    const { streams, bodies } = chat({ stop: 'hold' });
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(run.delta('Part')));
    await screen.findByText('Part');
    await userEvent.keyboard('{Escape}');

    act(() => {
      streams[0].push(cancelled(bodies[0].threadId, bodies[0].runId));
      streams[0].close();
    });

    expect(await screen.findByTestId('turn-stopped')).toBeInTheDocument();
    expect(screen.queryByTestId('turn-error')).not.toBeInTheDocument();
  });

  it('says it is stopping until the run says it has stopped, and asks only once', async () => {
    const { streams, stops } = chat({ stop: 'hold' });
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(run.delta('The fee')));
    await screen.findByText('The fee');

    await userEvent.keyboard('{Escape}');
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Stopping…');
    expect(screen.queryByTestId('turn-stopped')).not.toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(stops()).toHaveLength(1));

    // What the run still says reaches its turn.
    act(() => streams[0].push(run.delta(' schedule')));
    expect(await within(lastTurn()).findByText('The fee schedule')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Answering…' })).toBeDisabled();

    act(() => {
      streams[0].push(aborted());
      streams[0].close();
    });
    expect(await screen.findByTestId('turn-stopped')).toBeInTheDocument();
    expect(stops()).toHaveLength(1);
  });

  it('asks for the stop wherever the focus is on the page', async () => {
    const { streams, stops } = chat();
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(run.delta('Looking')));
    await screen.findByText('Looking');

    await userEvent.click(screen.getByRole('heading', { name: 'Chat' }));
    expect(screen.getByLabelText('Message')).not.toHaveFocus();
    await userEvent.keyboard('{Escape}');

    expect(await screen.findByTestId('turn-stopped')).toBeInTheDocument();
    expect(stops()).toHaveLength(1);
  });

  it('shows a tool call the runtime closed as stopped, not failed', async () => {
    const { streams } = chat({ stop: 'hold' });
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(...run.toolCall('c1', 'search_documents', '{}')));
    expect(await screen.findByTestId('tool-call-card')).toHaveAttribute('data-state', 'running');
    await userEvent.keyboard('{Escape}');

    act(() => {
      streams[0].push(stoppedResult('c1'), aborted());
      streams[0].close();
    });

    await screen.findByTestId('turn-stopped');
    const card = screen.getByTestId('tool-call-card');
    expect(card).toHaveAttribute('data-state', 'stopped');
    expect(card).toHaveTextContent('stopped');
  });

  it('shows a tool call the run left running as stopped', async () => {
    const { streams } = chat();
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(...run.toolCall('c1', 'search_documents', '{}')));
    expect(await screen.findByTestId('tool-call-card')).toHaveAttribute('data-state', 'running');

    await userEvent.keyboard('{Escape}');

    await screen.findByTestId('turn-stopped');
    expect(screen.getByTestId('tool-call-card')).toHaveAttribute('data-state', 'stopped');
  });

  it('asks for nothing when no run is in progress', async () => {
    const { streams, stops } = chat();
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => {
      streams[0].push(...run.text('All done.'), run.done());
      streams[0].close();
    });
    await waitFor(() => expect(screen.queryByTestId('stop-hint')).not.toBeInTheDocument());

    await userEvent.keyboard('{Escape}');

    expect(screen.getByText('All done.')).toBeInTheDocument();
    expect(screen.queryByTestId('turn-stopped')).not.toBeInTheDocument();
    expect(stops()).toHaveLength(0);
  });

  it('sends the next message in the same conversation, into a new turn', async () => {
    const { streams, bodies } = chat();
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    act(() => streams[0].push(run.delta('half')));
    await screen.findByText('half');
    await userEvent.keyboard('{Escape}');
    await screen.findByTestId('turn-stopped');

    await ask('try again');
    await waitFor(() => expect(streams).toHaveLength(2));
    act(() => {
      streams[1].push(...run.text('The full answer.'), run.done(bodies[0].threadId, 't2'));
      streams[1].close();
    });

    expect(await within(lastTurn()).findByText('The full answer.')).toBeInTheDocument();
    expect(screen.getAllByTestId('assistant-turn')).toHaveLength(2);
    expect(bodies[1].threadId).toBe(bodies[0].threadId);
  });

  it('says how to stop for as long as the run goes, and not once it is over', async () => {
    const { streams } = chat();
    await ask();
    await waitFor(() => expect(streams).toHaveLength(1));
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');
    act(() => streams[0].push(run.delta('Part of it')));
    await screen.findByText('Part of it');
    expect(screen.getByTestId('stop-hint')).toBeInTheDocument();
    act(() => {
      streams[0].push(run.done());
      streams[0].close();
    });
    await waitFor(() => expect(screen.queryByTestId('stop-hint')).not.toBeInTheDocument());

    await ask('again');
    await waitFor(() => expect(streams).toHaveLength(2));
    await screen.findByTestId('stop-hint');
    await userEvent.keyboard('{Escape}');
    await screen.findByTestId('turn-stopped');
    expect(screen.queryByTestId('stop-hint')).not.toBeInTheDocument();
  });
});
