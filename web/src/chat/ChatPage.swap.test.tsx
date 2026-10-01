import { EventType } from '@ag-ui/core';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { agentFetch } from '../test/agentFetch';
import { jsonResponse, renderWithProviders, sse, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

/**
 * An agent this web code was never written for, as any AG-UI agent would stream a run: it says back what it was told,
 * calls a tool of its own, takes a step and shows an activity of a kind this screen does not know.
 */
const echoAgent = (said: string) =>
  streamResponse([
    sse(EventType.RUN_STARTED, { threadId: 'echo-thread', runId: 'echo-run' }),
    sse(EventType.STEP_STARTED, { stepName: 'listening' }),
    sse(EventType.STEP_FINISHED, { stepName: 'listening' }),
    sse(EventType.TOOL_CALL_START, { toolCallId: 'w1', toolCallName: 'lookup_weather' }),
    sse(EventType.TOOL_CALL_ARGS, { toolCallId: 'w1', delta: '{"city":"Sofia"}' }),
    sse(EventType.TOOL_CALL_END, { toolCallId: 'w1' }),
    sse(EventType.TOOL_CALL_RESULT, { toolCallId: 'w1', messageId: 'w1r', content: 'sunny' }),
    sse(EventType.ACTIVITY_SNAPSHOT, {
      messageId: 'x1',
      activityType: 'someone-else/forecast',
      content: { temp: 21 },
    }),
    sse(EventType.TEXT_MESSAGE_START, { messageId: 'm1', role: 'assistant' }),
    sse(EventType.TEXT_MESSAGE_CONTENT, { messageId: 'm1', delta: `echo: ${said}` }),
    sse(EventType.TEXT_MESSAGE_END, { messageId: 'm1' }),
    sse(EventType.RUN_FINISHED, {
      threadId: 'echo-thread',
      runId: 'echo-run',
      outcome: { type: 'success' },
    }),
  ]);

describe('ChatPage with another agent behind it (agui-protocol-only)', () => {
  it('follows an agent it was never written for, from the protocol alone', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string, init?: RequestInit) =>
          url === '/api/chat'
            ? echoAgent(JSON.parse(String(init?.body)).messages.at(-1).content)
            : jsonResponse({ conversations: [], nextCursor: null }),
        ),
      ),
    );

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'hello there');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const turn = await screen.findByTestId('assistant-turn');
    // What it said, generically: the answer, and its tool call as a finished card under its own name.
    expect(await within(turn).findByText('echo: hello there')).toBeInTheDocument();
    const card = within(turn).getByTestId('tool-call-card');
    expect(card).toHaveTextContent('lookup_weather');
    // An activity of a kind this screen does not know is left out, and the run still ends.
    expect(within(turn).queryByTestId('data-card')).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Send' })).toBeInTheDocument());
    expect(within(turn).queryByTestId('turn-error')).not.toBeInTheDocument();
    expect(within(turn).queryByTestId('run-progress')).not.toBeInTheDocument();
  });
});
