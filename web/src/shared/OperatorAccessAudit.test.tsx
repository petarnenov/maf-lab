import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { useAuth } from '../plugins/api';
import type { Role } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders } from '../test';
import { OperatorAccessAudit } from './OperatorAccessAudit';

const entry = (id: number, operatorId: string) => ({
  id,
  at: '2026-10-09T12:00:00Z',
  operatorId,
});

function Switch({ tenant = 'firm-b' }: { tenant?: string }) {
  const { setSession } = useAuth();
  return (
    <button
      onClick={() =>
        setSession({ ...makeSession('TENANT_ADMIN', tenant), token: 'replacement-token' })
      }
    >
      Change session
    </button>
  );
}

describe('Operator entry audit', () => {
  it('reads authenticated metadata, pages by row id, and returns to the newest entries', async () => {
    const fetchMock = vi.fn(async (path: string) =>
      jsonResponse(
        path.endsWith('?before=51')
          ? { actions: [entry(50, 'earlier-operator')], nextCursor: null }
          : { actions: [entry(100, 'latest-operator')], nextCursor: 51 },
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(<OperatorAccessAudit />, { session: makeSession('TENANT_ADMIN') });
    expect(await screen.findByText('latest-operator')).toBeInTheDocument();
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'When',
      'Operator',
    ]);
    expect(screen.getByText(new Date(entry(100, '').at).toLocaleString())).toHaveAttribute(
      'datetime',
      entry(100, '').at,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Earlier entries' }));
    expect(await screen.findByText('earlier-operator')).toBeInTheDocument();
    expect(screen.queryByText('latest-operator')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Earlier entries' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Latest entries' }));
    expect(await screen.findByText('latest-operator')).toBeInTheDocument();
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/admin/operator-audit',
      '/api/admin/operator-audit?before=51',
      '/api/admin/operator-audit',
    ]);
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/admin/operator-audit',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer token-TENANT_ADMIN' }),
        signal: expect.any(AbortSignal),
      }),
    );
  });

  it('shows loading and empty states', async () => {
    let release!: (response: Response) => void;
    vi.stubGlobal(
      'fetch',
      vi.fn(
        () =>
          new Promise<Response>((resolve) => {
            release = resolve;
          }),
      ),
    );
    renderWithProviders(<OperatorAccessAudit />, { session: makeSession('TENANT_ADMIN') });
    expect(screen.getByRole('status')).toHaveTextContent('Loading operator entries');
    release(jsonResponse({ actions: [], nextCursor: null }));
    expect(await screen.findByText('No operator entries recorded.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('shows a content-free error and retains the way back from a failed older page', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (path: string) =>
        path.includes('?')
          ? jsonResponse({ error: 'token-and-private-content' }, 500)
          : jsonResponse({ actions: [entry(100, 'operator-a')], nextCursor: 51 }),
      ),
    );
    renderWithProviders(<OperatorAccessAudit />, { session: makeSession('TENANT_ADMIN') });
    await userEvent.click(await screen.findByRole('button', { name: 'Earlier entries' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Operator entries could not be loaded.',
    );
    expect(screen.queryByText('token-and-private-content')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Latest entries' }));
    expect(await screen.findByText('operator-a')).toBeInTheDocument();
  });

  it.each(['USER', 'READ_ONLY', 'PLATFORM_ADMIN'] as Role[])(
    'does not mount or read metadata for %s',
    (role) => {
      const fetchMock = vi.fn();
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(<OperatorAccessAudit />, { session: makeSession(role) });
      expect(screen.queryByRole('region', { name: 'Operator entries' })).not.toBeInTheDocument();
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it.each(['firm-a', 'firm-b'])(
    'resets pagination and hides old session data until the new read completes in %s',
    async (tenant) => {
      let release!: (response: Response) => void;
      const pending = new Promise<Response>((resolve) => {
        release = resolve;
      });
      const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
        if (
          (init?.headers as Record<string, string>).Authorization === 'Bearer replacement-token'
        ) {
          return pending;
        }
        return jsonResponse({
          actions: [entry(100, path.includes('?') ? 'old-page' : 'old-latest')],
          nextCursor: 51,
        });
      });
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(
        <>
          <OperatorAccessAudit />
          <Switch tenant={tenant} />
        </>,
        {
          session: makeSession('TENANT_ADMIN'),
        },
      );
      await userEvent.click(await screen.findByRole('button', { name: 'Earlier entries' }));
      expect(await screen.findByText('old-page')).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Change session' }));
      expect(screen.queryByText('old-page')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Latest entries' })).not.toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent('Loading operator entries');
      await waitFor(() =>
        expect(fetchMock).toHaveBeenLastCalledWith(
          '/api/admin/operator-audit',
          expect.objectContaining({
            headers: expect.objectContaining({ Authorization: 'Bearer replacement-token' }),
          }),
        ),
      );
      release(jsonResponse({ actions: [entry(90, 'new-session-operator')], nextCursor: null }));
      expect(await screen.findByText('new-session-operator')).toBeInTheDocument();
      expect(screen.queryByText('old-latest')).not.toBeInTheDocument();
    },
  );

  it('ignores a late response from the previous organization', async () => {
    let releaseOld!: (response: Response) => void;
    const pendingOld = new Promise<Response>((resolve) => {
      releaseOld = resolve;
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_path: string, init?: RequestInit) =>
        (init?.headers as Record<string, string>).Authorization === 'Bearer replacement-token'
          ? jsonResponse({ actions: [entry(2, 'new-operator')], nextCursor: null })
          : pendingOld,
      ),
    );
    renderWithProviders(
      <>
        <OperatorAccessAudit />
        <Switch />
      </>,
      { session: makeSession('TENANT_ADMIN') },
    );
    expect(screen.getByRole('status')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Change session' }));
    expect(await screen.findByText('new-operator')).toBeInTheDocument();
    await act(async () => {
      releaseOld(jsonResponse({ actions: [entry(1, 'stale-operator')], nextCursor: null }));
    });
    expect(screen.queryByText('stale-operator')).not.toBeInTheDocument();
    expect(screen.getByText('new-operator')).toBeInTheDocument();
  });
});
