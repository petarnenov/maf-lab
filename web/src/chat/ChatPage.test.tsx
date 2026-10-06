import { EventType } from '@ag-ui/core';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import {
  controlledStreamResponse,
  jsonResponse,
  renderWithProviders,
  run,
  sse,
  streamResponse,
} from '../test/render';
import { agentFetch } from '../test/agentFetch';
import { ChatPage } from './ChatPage';

const emptyHistory = { conversations: [], nextCursor: null };

describe('ChatPage', () => {
  it('streams a turn with a tool card, sources and feedback buttons', async () => {
    const fetchMock = vi.fn(async (url: string) =>
      url.startsWith('/api/conversations')
        ? jsonResponse(emptyHistory)
        : streamResponse([
            ...run.toolCall('c1', 'search_documents', 'sourceTypes=docs'),
            run.toolResult('c1', '2 snippets', 'search_documents', 2),
            run.sources([
              {
                docId: 'd1',
                sectionPath: 'Fees > Missing',
                sourcePath: 'shared/docs/fees.md',
                snippet: 's',
              },
            ]),
            run.delta('Open a ticket.'),
            run.done('conv-1', 't1'),
          ]),
    );
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const turn = await screen.findByTestId('assistant-turn');
    expect(await within(turn).findByText('Open a ticket.')).toBeInTheDocument();
    expect(within(turn).getByTestId('tool-call-card')).toHaveTextContent('2 sources');
    expect(within(turn).getByText('Sources (1)')).toBeInTheDocument();
    expect(within(turn).getByRole('button', { name: 'Wrong document' })).toBeInTheDocument();

    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/chat') as unknown as [
      string,
      RequestInit,
    ];
    // A turn is a run of the chat agent through CopilotKit: a thread the client names, a run, and only the new message
    // (the server keeps the conversation).
    const body = JSON.parse(init.body as string);
    expect(body.threadId).toMatch(/^c_[0-9a-f]{32}$/);
    expect(body.runId).toMatch(/^r-/);
    expect(body.messages).toEqual([
      {
        id: expect.stringMatching(/^u-/),
        role: 'user',
        content: 'What if a fee schedule is missing?',
      },
    ]);
  });

  it('asks for a persona when signed out', () => {
    renderWithProviders(<ChatPage />, { session: null });
    expect(screen.getByText(/Pick a dev persona/)).toBeInTheDocument();
  });
  it('shows the reasoning while the model thinks, then collapses it when the answer starts', async () => {
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

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    stream.push(run.started(), ...run.reasoning('The run failed ', 'on a missing schedule.'));
    const block = await screen.findByTestId('reasoning');
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'true');
    expect(block).toHaveTextContent('The run failed on a missing schedule.');
    expect(within(block).getByRole('button')).toHaveTextContent('Thinking…');

    stream.push(...run.text('Assign the schedule.'));
    expect(await screen.findByText('Assign the schedule.')).toBeInTheDocument();
    // The answer arrived, so the block closed itself and says how long the model thought.
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'false');
    expect(within(block).getByRole('button')).toHaveTextContent(/Thought for \d+\.\d s/);
    expect(block).not.toHaveTextContent('on a missing schedule.');

    stream.push(run.done('conv-1', 't1'));
  });

  it('keeps the reasoning open once a person opens it, and shows none for a turn without any', async () => {
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

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    stream.push(run.started(), ...run.reasoning('Thinking it over.'), run.delta('Assign it.'));
    const block = await screen.findByTestId('reasoning');
    await screen.findByText('Assign it.');
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(within(block).getByRole('button'));
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'true');

    // More of the answer must not take the block back.
    stream.push(run.delta(' Then re-run.'));
    expect(await screen.findByText('Assign it. Then re-run.')).toBeInTheDocument();
    expect(within(block).getByRole('button')).toHaveAttribute('aria-expanded', 'true');

    stream.push(run.done('conv-1', 't1'));
  });

  it('shows no reasoning block for a turn whose model did not reason', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async (url: string) =>
          url.startsWith('/api/conversations')
            ? jsonResponse(emptyHistory)
            : url.startsWith('/api/turns')
              ? jsonResponse({ events: [] })
              : streamResponse([run.started(), ...run.text('Straight to it.'), run.done()]),
        ),
      ),
    );

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'hello');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    await screen.findByText('Straight to it.');
    expect(screen.queryByTestId('reasoning')).not.toBeInTheDocument();
  });
});

describe('ChatPage progress', () => {
  it("shows what the run is doing from its steps, in the page's theme, until it ends", async () => {
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

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    stream.push(run.started(), sse(EventType.STEP_STARTED, { stepName: 'tool: search_documents' }));
    expect(
      await screen.findByRole('progressbar', { name: 'Calling search_documents…' }),
    ).toBeInTheDocument();

    stream.push(
      sse(EventType.STEP_FINISHED, { stepName: 'tool: search_documents' }),
      ...run.text('Assign it.'),
    );
    await screen.findByText('Assign it.');
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();

    stream.push(sse(EventType.STEP_STARTED, { stepName: 'checking the answer' }));
    expect(
      await screen.findByRole('progressbar', { name: 'Checking the answer…' }),
    ).toBeInTheDocument();

    stream.push(sse(EventType.STEP_FINISHED, { stepName: 'checking the answer' }), run.done());
    stream.close();
    await waitFor(() => expect(screen.queryByTestId('run-progress')).not.toBeInTheDocument());
  });
});
