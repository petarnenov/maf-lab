import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { A2AActivity } from './types';
import { jsonResponse, makeSession, renderWithProviders, RequireAdmin } from '@maf/testing';
import { A2AAdminPage } from './A2AAdminPage';

const activity: A2AActivity = {
  inbound: [
    {
      taskId: 'task-running-1234',
      partnerId: 'acme-portal',
      operation: 'a2a.message/send',
      state: 'Working',
      createdAt: '2026-09-20T12:00:00Z',
      updatedAt: '2026-09-20T12:00:05Z',
      durationMs: 5000,
      cancellable: true,
    },
    {
      taskId: 'task-done-5678',
      partnerId: 'acme-portal',
      operation: 'a2a.message/send',
      state: 'Completed',
      createdAt: '2026-09-20T11:00:00Z',
      updatedAt: '2026-09-20T11:00:09Z',
      durationMs: 9000,
      cancellable: false,
    },
  ],
  outbound: [
    {
      taskId: 'review-1',
      agent: 'compliance',
      outcome: 'approved',
      durationMs: 21000,
      at: '2026-09-20T11:30:00Z',
    },
    {
      taskId: 'review-2',
      agent: 'compliance',
      outcome: 'timeout',
      durationMs: 90000,
      at: '2026-09-20T11:40:00Z',
    },
  ],
  deliveries: [
    {
      taskId: 'task-done-5678',
      state: 'Completed',
      url: 'http://partner/hook',
      attempts: 1,
      delivered: true,
      error: null,
      at: '2026-09-20T11:00:09Z',
    },
    {
      taskId: 'task-done-5678',
      state: 'Working',
      url: 'http://partner/hook',
      attempts: 3,
      delivered: false,
      error: 'Connection refused',
      at: '2026-09-20T11:00:02Z',
    },
  ],
};

const empty: A2AActivity = { inbound: [], outbound: [], deliveries: [] };

/** The api as the page sees it: the activity. */
const serve = (body: A2AActivity) => vi.fn(async () => jsonResponse(body));

describe('A2AAdminPage', () => {
  it('stops a refresh still loading on Esc and keeps what it showed', async () => {
    let refreshing = false;
    const signals: AbortSignal[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((_url: string, init?: RequestInit) => {
        if (!refreshing) return Promise.resolve(jsonResponse(activity));
        if (init?.signal) signals.push(init.signal);
        return new Promise<Response>((_, reject) =>
          init?.signal?.addEventListener('abort', () =>
            reject(new DOMException('aborted', 'AbortError')),
          ),
        );
      }),
    );
    renderWithProviders(<A2AAdminPage />);
    expect(await screen.findByTestId('a2a-inbound')).toHaveTextContent('acme-portal');

    refreshing = true;
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(await screen.findByTestId('stop-hint')).toHaveTextContent('Esc to stop');
    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(signals.length > 0 && signals.every((s) => s.aborted)).toBe(true));
    expect(screen.getByTestId('a2a-inbound')).toHaveTextContent('acme-portal');
    await waitFor(() => expect(screen.queryByTestId('stop-hint')).toBeNull());
  });

  it('shows what arrived, what was asked and what was delivered', async () => {
    vi.stubGlobal('fetch', serve(activity));
    renderWithProviders(<A2AAdminPage />);

    expect(await screen.findByTestId('a2a-inbound')).toBeInTheDocument();
    expect(screen.getByTestId('a2a-inbound')).toHaveTextContent('acme-portal');
    expect(screen.getByTestId('a2a-inbound')).toHaveTextContent('Working');
    expect(screen.getByTestId('a2a-inbound')).toHaveTextContent('Completed');

    expect(screen.getByTestId('a2a-outbound')).toHaveTextContent('approved');
    // A consultation that produced no verdict is still listed, with what happened instead.
    expect(screen.getByTestId('a2a-outbound')).toHaveTextContent('timeout');

    expect(screen.getByTestId('a2a-deliveries')).toHaveTextContent('yes');
    expect(screen.getByTestId('a2a-deliveries')).toHaveTextContent(/no — Connection refused/);
  });

  it('offers to cancel only a task that is still running', async () => {
    vi.stubGlobal('fetch', serve(activity));
    renderWithProviders(<A2AAdminPage />);

    await screen.findByTestId('a2a-inbound');
    expect(screen.getAllByRole('button', { name: 'Cancel' })).toHaveLength(1);
  });

  it('cancels through the endpoint and reloads what happened', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        calls.push(`${init?.method ?? 'GET'} ${url}`);
        if (url.includes('/cancel'))
          return jsonResponse({ taskId: 'task-running-1234', state: 'Canceled' });
        return jsonResponse(activity);
      }),
    );
    renderWithProviders(<A2AAdminPage />);

    await screen.findByTestId('a2a-inbound');
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() =>
      expect(calls).toContain('POST /api/admin/a2a/tasks/task-running-1234/cancel'),
    );
    // The list is asked again, so what the screen shows is what the server now says.
    await waitFor(() =>
      expect(calls.filter((c) => c === 'GET /api/admin/a2a').length).toBeGreaterThan(1),
    );
  });

  it('is refused to an advisor, as the other admin screens are', async () => {
    vi.stubGlobal('fetch', serve(activity));
    renderWithProviders(
      <RequireAdmin>
        <A2AAdminPage />
      </RequireAdmin>,
      { session: makeSession('USER') },
    );

    expect(screen.getByText('Access denied')).toBeInTheDocument();
    expect(screen.queryByTestId('a2a-inbound')).not.toBeInTheDocument();
  });

  it('says so when no agent has talked to this system', async () => {
    vi.stubGlobal('fetch', serve(empty));
    renderWithProviders(<A2AAdminPage />);

    expect(await screen.findByTestId('a2a-empty')).toBeInTheDocument();
    expect(screen.queryByTestId('a2a-inbound')).not.toBeInTheDocument();
  });
  it('shows themed progress for each part while the api has not answered', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    );
    renderWithProviders(<A2AAdminPage />);

    expect(
      await screen.findByRole('progressbar', { name: 'Loading the A2A activity…' }),
    ).toBeInTheDocument();
    // A refresh cannot be started while the first answer is still coming.
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeDisabled();
  });

  it('refreshes the activity with one press', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => {
        calls.push(url);
        return jsonResponse(activity);
      }),
    );
    renderWithProviders(<A2AAdminPage />);

    await screen.findByTestId('a2a-inbound');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh' })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }));

    await waitFor(() => expect(calls.filter((c) => c === '/api/admin/a2a').length).toBe(2));
  });
});
