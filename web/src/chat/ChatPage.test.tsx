import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders, sse, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

describe('ChatPage', () => {
  it('streams a turn with a tool card, sources and feedback buttons', async () => {
    const fetchMock = vi.fn(async () =>
      streamResponse([
        sse('tool_call_started', {
          callId: 'c1',
          toolName: 'search_documents',
          argumentSummary: 'query="fee"',
        }),
        sse('tool_call_finished', {
          callId: 'c1',
          toolName: 'search_documents',
          resultSummary: '2 snippets',
          sourceCount: 2,
          isError: false,
        }),
        sse('sources', {
          sources: [
            {
              docId: 'd1',
              sectionPath: 'Fees > Missing',
              sourcePath: 'shared/docs/fees.md',
              snippet: 's',
            },
          ],
        }),
        sse('text_delta', { text: 'Open a ticket.' }),
        sse('done', { conversationId: 'conv-1', turnId: 't1' }),
      ]),
    );
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<ChatPage />);
    await userEvent.type(screen.getByLabelText('Message'), 'What if a fee schedule is missing?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const turn = await screen.findByTestId('assistant-turn');
    expect(await within(turn).findByText('Open a ticket.')).toBeInTheDocument();
    expect(within(turn).getByTestId('tool-call-card')).toHaveTextContent('2 sources');
    expect(within(turn).getByText('Sources (1)')).toBeInTheDocument();
    expect(within(turn).getByRole('button', { name: 'Wrong document' })).toBeInTheDocument();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/chat');
    expect(JSON.parse(init.body as string)).toEqual({
      message: 'What if a fee schedule is missing?',
    });
  });

  it('asks for a persona when signed out', () => {
    renderWithProviders(<ChatPage />, { session: null });
    expect(screen.getByText(/Pick a dev persona/)).toBeInTheDocument();
  });
});
