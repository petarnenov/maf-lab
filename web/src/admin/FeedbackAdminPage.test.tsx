import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ReviewQueueItem } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { FeedbackAdminPage } from './FeedbackAdminPage';

const item: ReviewQueueItem = {
  turnId: 'turn-42',
  conversationId: 'conv-1',
  userId: 'u-advisor',
  question: 'What is the procedure when a fee schedule is missing?',
  answer: 'Something unrelated.',
  signals: ['negative_feedback', 'zero_retrieval_results'],
  toolCalls: [
    {
      toolName: 'search_documents',
      argumentSummary: 'q',
      outcome: 'ok',
      sourceCount: 2,
      docIds: ['doc-fees'],
      chunkIds: ['doc-fees#0', 'doc-fees#3'],
    },
  ],
  feedbackKinds: ['wrong_document'],
  createdAt: '2026-09-19T10:00:00Z',
  labeled: false,
};

describe('FeedbackAdminPage', () => {
  it('shows flagged turns with their signals', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([item])),
    );
    renderWithProviders(<FeedbackAdminPage />, { session: makeSession('FIRM_ADMIN') });
    expect(await screen.findByText(item.question)).toBeInTheDocument();
    expect(screen.getByText('zero retrieval results')).toBeInTheDocument();
    expect(screen.getByText('negative feedback')).toBeInTheDocument();
  });

  it('submits a retrieval label to the append-to-dataset endpoint', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/admin/feedback/queue') return jsonResponse([item]);
      if (init?.method === 'POST') return new Response(null, { status: 204 });
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<FeedbackAdminPage />, { session: makeSession('FIRM_ADMIN') });
    await userEvent.click(await screen.findByText(item.question));

    expect(screen.getByRole('combobox', { name: 'Dataset' })).toHaveValue('retrieval');
    await userEvent.click(screen.getByLabelText('doc-fees#3'));
    await userEvent.type(
      screen.getByLabelText(/Other relevant chunk ids/),
      'chunk-9{enter}doc-fees#3',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Append to dataset' }));

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent('Label saved for turn turn-42'),
    );
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!;
    expect(post[0]).toBe('/api/admin/feedback/turn-42/label');
    expect(JSON.parse(post[1]!.body as string)).toEqual({
      dataset: 'retrieval',
      relevantChunkIds: ['doc-fees#3', 'chunk-9'],
    });
  });

  it('submits a selection label with the checked tools', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) =>
      init?.method === 'POST'
        ? new Response(null, { status: 204 })
        : jsonResponse(url.endsWith('queue') ? [item] : {}),
    );
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<FeedbackAdminPage />, { session: makeSession('FIRM_ADMIN') });
    await userEvent.click(await screen.findByText(item.question));
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Dataset' }), 'selection');
    await userEvent.click(screen.getByLabelText('get_billing_run_status'));
    await userEvent.click(screen.getByRole('button', { name: 'Append to dataset' }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(true),
    );
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!;
    expect(JSON.parse(post[1]!.body as string)).toEqual({
      dataset: 'selection',
      expectedTools: ['search_documents', 'get_billing_run_status'],
    });
  });
});

describe('LabelForm retrieval without returned chunks', () => {
  it('requires a free-text chunk id when the turn returned none', async () => {
    const empty: ReviewQueueItem = {
      ...item,
      toolCalls: [{ ...item.toolCalls[0], sourceCount: 0, docIds: [], chunkIds: [] }],
    };
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([empty])),
    );
    renderWithProviders(<FeedbackAdminPage />, { session: makeSession('FIRM_ADMIN') });
    await userEvent.click(await screen.findByText(item.question));

    expect(screen.getByText('This turn returned no chunks.')).toBeInTheDocument();
    const submit = screen.getByRole('button', { name: 'Append to dataset' });
    expect(submit).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Other relevant chunk ids/), 'chunk-1');
    expect(submit).toBeEnabled();
  });
});
