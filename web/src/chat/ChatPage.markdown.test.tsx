import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import type { ConversationDetail } from '../api/types';
import { jsonResponse, renderWithProviders, run, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

const answer =
  '**FS-REQUIRED**: assign the schedule.\n\n1. Open the failed run\n2. Assign the schedule\n3. Re-run';

describe('ChatPage markdown answers', () => {
  it('renders a streamed answer as markdown, with no raw markers left', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url === '/api/chat'
          ? streamResponse([
              run.delta(answer.slice(0, 30)),
              run.delta(answer.slice(30)),
              run.done(),
            ])
          : jsonResponse({}, 404),
      ),
    );
    renderWithProviders(<ChatPage />);

    await userEvent.type(screen.getByLabelText('Message'), 'what if a fee schedule is missing');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const turn = await screen.findByTestId('assistant-turn');
    expect(await within(turn).findByText('FS-REQUIRED')).toHaveProperty('tagName', 'STRONG');
    expect(within(turn).getAllByRole('listitem')).toHaveLength(3);
    expect(turn.textContent).not.toContain('**');
    // The question stays plain text.
    expect(
      screen.getByText('what if a fee schedule is missing').querySelector('strong'),
    ).toBeNull();
  });

  it('renders a restored answer the same way', async () => {
    const detail: ConversationDetail = {
      conversationId: 'conv-5',
      title: 'Missing schedule',
      createdAt: '2026-09-29T09:00:00Z',
      lastActivityAt: '2026-09-29T09:00:00Z',
      turns: [
        {
          turnId: 't1',
          question: 'what if a fee schedule is missing',
          answer,
          createdAt: '2026-09-29T09:00:00Z',
          toolCalls: [],
          sources: [],
          feedbackKinds: [],
          traceAvailable: false,
        },
      ],
    };
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        if (url.startsWith('/api/conversations?'))
          return jsonResponse({ conversations: [], nextCursor: null });
        if (url === '/api/conversations/conv-5') return jsonResponse(detail);
        return jsonResponse({}, 404);
      }),
    );
    renderWithProviders(
      <Routes>
        <Route path="chat/:conversationId?" element={<ChatPage />} />
      </Routes>,
      { route: '/chat/conv-5' },
    );

    const turn = await screen.findByTestId('assistant-turn');
    expect(await within(turn).findByText('FS-REQUIRED')).toHaveProperty('tagName', 'STRONG');
    expect(within(turn).getAllByRole('listitem')).toHaveLength(3);
  });
});
