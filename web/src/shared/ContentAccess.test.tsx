import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useAuth } from '../plugins/api';
import type { Role } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders } from '../test';
import { ContentAccessBanner } from './ContentAccessBanner';
import type { ContentAccessGrant, ContentAccessState } from './contentAccess';
import { OperatorContentAccess } from './OperatorContentAccess';
import { PlatformContentAccess } from './PlatformContentAccess';

const grant = (id = 100, operatorId = 'operator-a'): ContentAccessGrant => ({
  id,
  operatorId,
  reason: 'INC-123',
  startedAt: '2026-10-09T12:00:00Z',
  expiresAt: '2026-10-09T13:00:00Z',
  endRequestedAt: null,
  endedAt: null,
});

function Switch({ role = 'PLATFORM_ADMIN', tenant = 'firm-b' }: { role?: Role; tenant?: string }) {
  const { setSession } = useAuth();
  return (
    <button
      onClick={() => setSession({ ...makeSession(role, tenant), token: 'replacement-token' })}
    >
      Change session
    </button>
  );
}

const renderPlatform = () =>
  renderWithProviders(
    <>
      <ContentAccessBanner />
      <PlatformContentAccess />
    </>,
    { session: makeSession('PLATFORM_ADMIN') },
  );

afterEach(() => vi.useRealTimers());

describe('Temporary operator content access', () => {
  it('requires an explicit, valid confirmation, submits bounded metadata, and displays fixed times', async () => {
    let state: ContentAccessState = { grant: null, active: false };
    const fetchMock = vi.fn(async (_path: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        state = { grant: grant(), active: true };
      }
      return jsonResponse(state, init?.method === 'POST' ? 201 : 200);
    });
    vi.stubGlobal('fetch', fetchMock);
    renderPlatform();
    await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
    const dialog = screen.getByRole('dialog');
    expect(screen.getByLabelText('Reason reference')).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Confirm content access' })).toBeDisabled();
    expect(screen.getByLabelText('Duration in minutes')).toHaveValue(60);
    await userEvent.type(screen.getByLabelText('Reason reference'), 'INC-123');
    expect(fetchMock.mock.calls.every(([, init]) => init?.method !== 'POST')).toBe(true);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm content access' }));
    expect(await screen.findByRole('button', { name: 'End content access' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/platform/content-access',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ Authorization: 'Bearer token-PLATFORM_ADMIN' }),
        body: JSON.stringify({ reason: 'INC-123', durationMinutes: 60 }),
      }),
    );
    const banner = screen.getByRole('complementary', { name: 'Platform operator access' });
    expect(banner).toHaveTextContent('Content access is active');
    expect(banner).toHaveTextContent('INC-123');
    expect(within(banner).getByText(new Date(grant().expiresAt).toLocaleString())).toHaveAttribute(
      'datetime',
      grant().expiresAt,
    );
    expect(screen.queryByRole('button', { name: /renew|extend/i })).not.toBeInTheDocument();
  });

  it.each(['Escape', 'Cancel'])(
    '%s before confirmation restores focus and sends no write',
    async (action) => {
      const fetchMock = vi.fn<(path: string, init?: RequestInit) => Promise<Response>>(async () =>
        jsonResponse({ grant: null, active: false }),
      );
      vi.stubGlobal('fetch', fetchMock);
      renderPlatform();
      const request = await screen.findByRole('button', { name: 'Request content access' });
      await userEvent.click(request);
      await userEvent.type(screen.getByLabelText('Reason reference'), 'INC-123');
      if (action === 'Escape') {
        await userEvent.keyboard('{Escape}');
      } else {
        await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      }
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(request).toHaveFocus();
      expect(fetchMock.mock.calls.every(([, init]) => init?.method !== 'POST')).toBe(true);
    },
  );

  it.each(['x', 'Ticket and private content', '<private>', ' INC-123', 'x'.repeat(129)])(
    'does not allow the invalid reason %j to be confirmed',
    async (reason) => {
      const fetchMock = vi.fn<(path: string, init?: RequestInit) => Promise<Response>>(async () =>
        jsonResponse({ grant: null, active: false }),
      );
      vi.stubGlobal('fetch', fetchMock);
      renderPlatform();
      await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
      fireEvent.change(screen.getByLabelText('Reason reference'), { target: { value: reason } });
      expect(screen.getByRole('button', { name: 'Confirm content access' })).toBeDisabled();
      expect(fetchMock.mock.calls.every(([, init]) => init?.method !== 'POST')).toBe(true);
    },
  );

  it.each(['', '0', '61', '1.5', '-1', '1e1'])(
    'does not confirm a duration outside integer minutes 1–60: %j',
    async (duration) => {
      vi.stubGlobal(
        'fetch',
        vi.fn(async () => jsonResponse({ grant: null, active: false })),
      );
      renderPlatform();
      await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
      await userEvent.type(screen.getByLabelText('Reason reference'), 'INC-123');
      fireEvent.change(screen.getByLabelText('Duration in minutes'), {
        target: { value: duration },
      });
      expect(screen.getByRole('button', { name: 'Confirm content access' })).toBeDisabled();
    },
  );

  it('traps keyboard focus within the confirmation', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ grant: null, active: false })),
    );
    renderPlatform();
    await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
    const input = screen.getByLabelText('Reason reference');
    await userEvent.type(input, 'INC-123');
    await userEvent.tab({ shift: true });
    expect(screen.getByRole('button', { name: 'Confirm content access' })).toHaveFocus();
    await userEvent.tab();
    expect(input).toHaveFocus();
  });

  it('prevents duplicate writes and holds the dialog while confirmation is pending', async () => {
    let release!: (response: Response) => void;
    const pending = new Promise<Response>((resolve) => {
      release = resolve;
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_path: string, init?: RequestInit) =>
        init?.method === 'POST' ? pending : jsonResponse({ grant: null, active: false }),
      ),
    );
    renderPlatform();
    await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
    await userEvent.type(screen.getByLabelText('Reason reference'), 'INC-123');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm content access' }));
    expect(screen.getByRole('button', { name: 'Confirm content access' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    await userEvent.keyboard('{Escape}');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    await act(async () => release(jsonResponse({ grant: grant(), active: true }, 201)));
  });

  it('reports a conflicting grant without exposing the error body or offering extension', async () => {
    let state: ContentAccessState = { grant: null, active: false };
    const fetchMock = vi.fn(async (_path: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        state = { grant: grant(), active: true };
        return jsonResponse({ error: 'private-content-conflict' }, 409);
      }
      return jsonResponse(state);
    });
    vi.stubGlobal('fetch', fetchMock);
    renderPlatform();
    await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
    await userEvent.type(screen.getByLabelText('Reason reference'), 'INC-123');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm content access' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('An existing grant must finish');
    expect(screen.queryByText('private-content-conflict')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirm content access' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: /renew|extend/i })).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
  });

  it('keeps the banner through expiry and a pending end; only a stored endedAt clears it', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    let state: ContentAccessState = {
      grant: { ...grant(), expiresAt: '2000-01-01T00:00:00Z' },
      active: false,
    };
    const fetchMock = vi.fn(async (_path: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        state = {
          ...state,
          grant: { ...state.grant!, endRequestedAt: '2026-10-09T13:00:00Z' },
        };
        return jsonResponse({ error: 'private-pending-details' }, 503);
      }
      return jsonResponse(state);
    });
    vi.stubGlobal('fetch', fetchMock);
    renderPlatform();
    const banner = await screen.findByRole('complementary', { name: 'Platform operator access' });
    await waitFor(() => expect(banner).toHaveTextContent('completion is not confirmed'));
    await userEvent.click(screen.getByRole('button', { name: 'End content access' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ending content access has not been confirmed',
    );
    expect(banner).toHaveTextContent('completion is not confirmed');
    expect(screen.queryByText('private-pending-details')).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/platform/content-access/100/end',
      expect.objectContaining({ method: 'POST', body: undefined }),
    );
    state = {
      active: false,
      grant: { ...state.grant!, endedAt: '2026-10-09T13:00:05Z' },
    };
    await act(async () => vi.advanceTimersByTime(5000));
    await waitFor(() => expect(banner).not.toHaveTextContent('completion is not confirmed'));
    expect(screen.getByRole('button', { name: 'Request content access' })).toBeEnabled();
    expect(screen.getByText(/Content access ended/)).toBeInTheDocument();
  });

  it('retains the active grant on an authorization-store outage and disables a new request', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    let fail = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        fail
          ? jsonResponse({ error: 'private-store-detail' }, 503)
          : jsonResponse({ grant: grant(), active: true }),
      ),
    );
    renderPlatform();
    const banner = screen.getByRole('complementary', { name: 'Platform operator access' });
    await waitFor(() => expect(banner).toHaveTextContent('Content access is active'));
    fail = true;
    await act(async () => vi.advanceTimersByTime(5000));
    await waitFor(() => expect(within(banner).getByRole('alert')).toBeInTheDocument());
    expect(banner).toHaveTextContent('INC-123');
    expect(banner).toHaveTextContent('Completion has not been acknowledged');
    expect(screen.queryByText('private-store-detail')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Request content access' }),
    ).not.toBeInTheDocument();
  });

  it('does not infer an ended grant when the initial read fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ error: 'private-store-detail' }, 503)),
    );
    renderPlatform();
    await waitFor(() => expect(screen.getAllByRole('alert')).toHaveLength(2));
    expect(screen.getByRole('button', { name: 'Request content access' })).toBeDisabled();
    expect(screen.queryByText(/Content access ended/)).not.toBeInTheDocument();
  });

  it.each(['USER', 'READ_ONLY', 'TENANT_ADMIN'] as Role[])(
    'does not read or offer platform content access for %s',
    (role) => {
      const fetchMock = vi.fn();
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(
        <>
          <ContentAccessBanner />
          <PlatformContentAccess />
        </>,
        { session: makeSession(role) },
      );
      expect(fetchMock).not.toHaveBeenCalled();
      expect(screen.queryByRole('region')).not.toBeInTheDocument();
      expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
    },
  );

  it('hides the previous operator grant and ignores its late response after token replacement', async () => {
    let releaseOld!: (response: Response) => void;
    const old = new Promise<Response>((resolve) => {
      releaseOld = resolve;
    });
    const fetchMock = vi.fn(async (_path: string, init?: RequestInit) =>
      (init?.headers as Record<string, string>).Authorization === 'Bearer replacement-token'
        ? jsonResponse({ grant: { ...grant(200, 'operator-b'), reason: 'NEW-456' }, active: true })
        : old,
    );
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(
      <>
        <ContentAccessBanner />
        <PlatformContentAccess />
        <Switch tenant="firm-a" />
      </>,
      { session: makeSession('PLATFORM_ADMIN') },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Change session' }));
    const banner = screen.getByRole('complementary', { name: 'Platform operator access' });
    await waitFor(() => expect(banner).toHaveTextContent('NEW-456'));
    await act(async () => releaseOld(jsonResponse({ grant: grant(), active: true })));
    expect(banner).not.toHaveTextContent('INC-123');
    expect(banner).toHaveTextContent('NEW-456');
  });

  it('a late confirmation from the old token cannot replace the new operator grant', async () => {
    let releaseWrite!: (response: Response) => void;
    const oldWrite = new Promise<Response>((resolve) => {
      releaseWrite = resolve;
    });
    const fetchMock = vi.fn(async (_path: string, init?: RequestInit) => {
      if ((init?.headers as Record<string, string>).Authorization === 'Bearer replacement-token') {
        return jsonResponse({ grant: { ...grant(200), reason: 'NEW-456' }, active: true });
      }
      return init?.method === 'POST' ? oldWrite : jsonResponse({ grant: null, active: false });
    });
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(
      <>
        <ContentAccessBanner />
        <PlatformContentAccess />
        <Switch />
      </>,
      { session: makeSession('PLATFORM_ADMIN') },
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
    await userEvent.type(screen.getByLabelText('Reason reference'), 'INC-123');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm content access' }));
    fireEvent.click(screen.getByRole('button', { name: 'Change session' }));
    const banner = screen.getByRole('complementary', { name: 'Platform operator access' });
    await waitFor(() => expect(banner).toHaveTextContent('NEW-456'));
    await act(async () => releaseWrite(jsonResponse({ grant: grant(), active: true }, 201)));
    expect(banner).toHaveTextContent('NEW-456');
    expect(banner).not.toHaveTextContent('INC-123');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
  });
});

describe('Tenant operator content access metadata', () => {
  it('shows who, why and fixed start/expiry/end, pages by row id, and never renders extra content', async () => {
    const fetchMock = vi.fn(async (path: string) =>
      jsonResponse({
        grants: path.includes('?')
          ? [{ ...grant(50, 'earlier-operator'), endedAt: '2026-10-09T12:30:00Z' }]
          : [{ ...grant(), privateContent: 'private-content' }],
        nextCursor: path.includes('?') ? null : 51,
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(<OperatorContentAccess />, { session: makeSession('TENANT_ADMIN') });
    expect(await screen.findByText('operator-a')).toBeInTheDocument();
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Operator',
      'Reason',
      'Started',
      'Expires',
      'Ended',
    ]);
    expect(screen.queryByText('private-content')).not.toBeInTheDocument();
    expect(screen.getByText('Not confirmed ended')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Earlier content access' }));
    expect(await screen.findByText('earlier-operator')).toBeInTheDocument();
    expect(screen.getByText(new Date('2026-10-09T12:30:00Z').toLocaleString())).toHaveAttribute(
      'datetime',
      '2026-10-09T12:30:00Z',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Latest content access' }));
    expect(await screen.findByText('operator-a')).toBeInTheDocument();
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/admin/content-access',
      '/api/admin/content-access?before=51',
      '/api/admin/content-access',
    ]);
  });

  it('shows the empty metadata state', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ grants: [], nextCursor: null })),
    );
    renderWithProviders(<OperatorContentAccess />, { session: makeSession('TENANT_ADMIN') });
    expect(await screen.findByText('No operator content access recorded.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('keeps pagination recovery after a content-free error', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (path: string) =>
        path.includes('?')
          ? jsonResponse({ error: 'private-details' }, 500)
          : jsonResponse({ grants: [grant()], nextCursor: 51 }),
      ),
    );
    renderWithProviders(<OperatorContentAccess />, { session: makeSession('TENANT_ADMIN') });
    await userEvent.click(await screen.findByRole('button', { name: 'Earlier content access' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Operator content access could not be loaded',
    );
    expect(screen.queryByText('private-details')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Latest content access' }));
    expect(await screen.findByText('operator-a')).toBeInTheDocument();
  });

  it.each(['USER', 'READ_ONLY', 'PLATFORM_ADMIN'] as Role[])(
    'never mounts or reads tenant grant metadata for %s',
    (role) => {
      const fetchMock = vi.fn();
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(<OperatorContentAccess />, { session: makeSession(role) });
      expect(fetchMock).not.toHaveBeenCalled();
      expect(screen.queryByRole('region')).not.toBeInTheDocument();
    },
  );

  it.each(['firm-a', 'firm-b'])(
    'resets paging and hides old rows after a new token in %s',
    async (tenant) => {
      let release!: (response: Response) => void;
      const pending = new Promise<Response>((resolve) => {
        release = resolve;
      });
      const fetchMock = vi.fn(async (path: string, init?: RequestInit) =>
        (init?.headers as Record<string, string>).Authorization === 'Bearer replacement-token'
          ? pending
          : jsonResponse({
              grants: [grant(100, path.includes('?') ? 'old-page' : 'old-latest')],
              nextCursor: 51,
            }),
      );
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(
        <>
          <OperatorContentAccess />
          <Switch role="TENANT_ADMIN" tenant={tenant} />
        </>,
        { session: makeSession('TENANT_ADMIN') },
      );
      await userEvent.click(await screen.findByRole('button', { name: 'Earlier content access' }));
      expect(await screen.findByText('old-page')).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Change session' }));
      expect(screen.queryByText('old-page')).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: 'Latest content access' }),
      ).not.toBeInTheDocument();
      await waitFor(() =>
        expect(fetchMock).toHaveBeenLastCalledWith(
          '/api/admin/content-access',
          expect.objectContaining({
            headers: expect.objectContaining({ Authorization: 'Bearer replacement-token' }),
          }),
        ),
      );
      await act(async () =>
        release(jsonResponse({ grants: [grant(200, 'new-operator')], nextCursor: null })),
      );
      expect(await screen.findByText('new-operator')).toBeInTheDocument();
    },
  );
});
