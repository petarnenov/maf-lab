import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import type { ConversationDetail } from '../api/types';
import { jsonResponse, renderWithProviders, run, streamResponse } from '../test/render';
import { agentFetch } from '../test/agentFetch';
import { ChatPage } from './ChatPage';

/** A stored conversation whose three questions are the history the arrow keys walk. */
const detail: ConversationDetail = {
  conversationId: 'conv-9',
  title: 'Three questions',
  createdAt: '2026-09-29T09:00:00Z',
  lastActivityAt: '2026-09-29T09:10:00Z',
  turns: ['first', 'second', 'third'].map((question, i) => ({
    turnId: `t${i + 1}`,
    question,
    answer: `Answer ${i + 1}.`,
    createdAt: `2026-09-29T09:0${i}:00Z`,
    toolCalls: [],
    sources: [],
    feedbackKinds: [],
  })),
};

function stubApi() {
  const fetchMock = vi.fn(async (url: string) => {
    if (url.startsWith('/api/conversations?'))
      return jsonResponse({ conversations: [], nextCursor: null });
    if (url === '/api/conversations/conv-9') return jsonResponse(detail);
    if (url === '/api/chat') return streamResponse([run.delta('Sent.'), run.done('conv-9', 't9')]);
    return jsonResponse({}, 404);
  });
  vi.stubGlobal('fetch', agentFetch(fetchMock));
  return fetchMock;
}

async function openStored() {
  stubApi();
  renderWithProviders(
    <Routes>
      <Route path="chat/:conversationId?" element={<ChatPage />} />
    </Routes>,
    { route: '/chat/conv-9' },
  );
  await screen.findByText('Answer 3.');
  const input = screen.getByLabelText<HTMLTextAreaElement>('Message');
  await userEvent.click(input);
  return input;
}

describe('ChatPage prompt recall', () => {
  it('steps back through the conversation’s prompts and stops at the oldest', async () => {
    const input = await openStored();

    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveValue('third');
    // The caret goes to the end of the recalled text.
    expect(input.selectionStart).toBe('third'.length);
    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveValue('second');
    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveValue('first');
    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveValue('first');
  });

  it('steps forward and past the newest prompt restores the draft', async () => {
    const input = await openStored();
    await userEvent.type(input, 'half a question');

    await userEvent.keyboard('{ArrowUp}{ArrowUp}');
    expect(input).toHaveValue('second');
    await userEvent.keyboard('{ArrowDown}');
    expect(input).toHaveValue('third');
    await userEvent.keyboard('{ArrowDown}');
    expect(input).toHaveValue('half a question');
  });

  it('recalls nothing when the conversation has no prompt yet', async () => {
    stubApi();
    renderWithProviders(
      <Routes>
        <Route path="chat/:conversationId?" element={<ChatPage />} />
      </Routes>,
      { route: '/chat' },
    );
    const input = screen.getByLabelText<HTMLTextAreaElement>('Message');
    await userEvent.click(input);

    await userEvent.keyboard('{ArrowUp}');

    expect(input).toHaveValue('');
  });

  it('leaves the caret to move when it is not on the first line', async () => {
    const input = await openStored();
    await userEvent.type(input, 'line one{Shift>}{Enter}{/Shift}line two');

    await userEvent.keyboard('{ArrowUp}');

    expect(input).toHaveValue('line one\nline two');
  });

  it('does not recall while a modifier key is held', async () => {
    const input = await openStored();

    await userEvent.keyboard('{Shift>}{ArrowUp}{/Shift}');

    expect(input).toHaveValue('');
  });

  it('ends the recall when the recalled text is edited', async () => {
    const input = await openStored();
    await userEvent.keyboard('{ArrowUp}{ArrowUp}');
    await userEvent.type(input, ', for firm B');
    expect(input).toHaveValue('second, for firm B');

    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveValue('third');
    await userEvent.keyboard('{ArrowDown}');
    expect(input).toHaveValue('second, for firm B');
  });

  it('ends the recall on sending, and the prompt just sent is the newest', async () => {
    const input = await openStored();
    await userEvent.keyboard('{ArrowUp}{ArrowUp}');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('Sent.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Send' })).toBeInTheDocument());

    await userEvent.click(input);
    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveValue('second');
    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveValue('third');
  });
});
