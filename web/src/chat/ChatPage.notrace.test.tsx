import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { agentFetch } from '../test/agentFetch';
import { renderChat } from '../test/chat';
import { jsonResponse, run, streamResponse } from '../test/render';

/**
 * Without the monitor plugin a turn is observed by nobody (introduce-plugins 5.3): the core's chat page asks for no
 * trace, live or stored. The test runtime would answer a trace request silently, so the oracle is the requests made.
 */
describe('ChatPage without the monitor', () => {
  it('asks for no trace while a turn runs or after it', async () => {
    const fetchMock = vi.fn(async (url: string) =>
      url.startsWith('/api/conversations')
        ? jsonResponse({ conversations: [], nextCursor: null })
        : url === '/api/chat'
          ? streamResponse([
              run.started(),
              ...run.text('Assign the schedule.'),
              run.done('conv-1', 't1'),
            ])
          : jsonResponse({}, 404),
    );
    // Every request the page makes, before the test runtime answers any of them (it answers a trace request itself).
    const runtime = agentFetch(fetchMock);
    const requests = vi.fn((url: string, init?: RequestInit) => runtime(url, init));
    vi.stubGlobal('fetch', requests);

    renderChat([]);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('Assign the schedule.');
    await new Promise((resolve) => setTimeout(resolve, 600));

    const urls = requests.mock.calls.map(([url]) => String(url));
    // The turn went through CopilotKit's runtime, as the page reaches the agent.
    expect(urls.some((url) => url.includes('/copilotkit/agent/chat/run'))).toBe(true);
    expect(urls.filter((url) => /\/api\/(runs|turns)\/[^/?]+\/trace/.test(url))).toEqual([]);
  });
});
