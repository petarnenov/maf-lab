import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { useAuth } from '@maf/plugin-api';
import { App, PluginsProvider, jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import platformAdmin from './index';

const grant = {
  id: 1,
  operatorId: 'company-operator',
  reason: 'INC-123',
  startedAt: '2026-10-09T12:00:00Z',
  expiresAt: '2026-10-09T13:00:00Z',
  endRequestedAt: null,
  endedAt: null,
};
const modules = { 'platform-admin': async () => ({ default: platformAdmin }) };

describe('Platform content access with actual module registration', () => {
  it('the real platform dashboard confirms access and keeps the global banner on a contributor page', async () => {
    let active = false;
    const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
      if (path === '/api/plugins') return jsonResponse({ plugins: [{ name: 'platform-admin' }] });
      if (path === '/api/platform/content-access') {
        if (init?.method === 'POST') active = true;
        return jsonResponse(
          { grant: active ? grant : null, active },
          init?.method === 'POST' ? 201 : 200,
        );
      }
      if (path === '/api/platform/plugin-audit')
        return jsonResponse({ actions: [], nextCursor: null });
      return jsonResponse([]);
    });
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(
      <PluginsProvider modules={modules}>
        <App />
      </PluginsProvider>,
      { session: makeSession('PLATFORM_ADMIN', 'firm-b'), route: '/platform' },
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
    await userEvent.type(screen.getByLabelText('Reason reference'), 'INC-123');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm content access' }));
    const banner = screen.getByRole('complementary', {
      name: 'Platform operator access',
    });
    await waitFor(() => expect(banner).toHaveTextContent('Content access is active'));
    expect(banner).toHaveTextContent('organization firm-b');
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/platform/content-access',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ reason: 'INC-123', durationMinutes: 60 }),
        headers: expect.objectContaining({
          Authorization: 'Bearer token-PLATFORM_ADMIN',
        }),
      }),
    );
    expect(
      fetchMock.mock.calls.some(([path]) => path.startsWith('/api/admin/content-access')),
    ).toBe(false);
    await userEvent.click(screen.getByRole('link', { name: 'Chat' }));
    expect(
      screen.queryByRole('region', { name: 'Temporary content access' }),
    ).not.toBeInTheDocument();
    expect(banner).toHaveTextContent('Content access is active');
  });

  it.each(['USER', 'READ_ONLY', 'TENANT_ADMIN'] as const)(
    '%s cannot read or request a platform grant through the real route',
    async (role) => {
      const fetchMock = vi.fn(async () => jsonResponse({ plugins: [{ name: 'platform-admin' }] }));
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(
        <PluginsProvider modules={modules}>
          <App />
        </PluginsProvider>,
        { session: makeSession(role), route: '/platform' },
      );
      expect(await screen.findByRole('alert')).toHaveTextContent(
        'This page requires the PLATFORM_ADMIN role',
      );
      expect(
        screen.queryByRole('region', { name: 'Temporary content access' }),
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole('complementary', {
          name: 'Platform operator access',
        }),
      ).not.toBeInTheDocument();
      expect(fetchMock).toHaveBeenCalledTimes(1);
    },
  );

  it('replaces the operator token during registration and never displays the old grant or pending confirmation', async () => {
    let releaseRegistration!: (response: Response) => void;
    let releaseGrant!: (response: Response) => void;
    const registration = new Promise<Response>((resolve) => {
      releaseRegistration = resolve;
    });
    const nextGrant = new Promise<Response>((resolve) => {
      releaseGrant = resolve;
    });
    const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
      const replacement =
        (init?.headers as Record<string, string>).Authorization === 'Bearer replacement-token';
      if (path === '/api/plugins')
        return replacement ? registration : jsonResponse({ plugins: [{ name: 'platform-admin' }] });
      if (path === '/api/platform/content-access')
        return replacement ? nextGrant : jsonResponse({ grant: null, active: false });
      if (path === '/api/platform/plugin-audit')
        return jsonResponse({ actions: [], nextCursor: null });
      return jsonResponse([]);
    });
    vi.stubGlobal('fetch', fetchMock);
    function Switch() {
      const { setSession } = useAuth();
      return (
        <button
          onClick={() =>
            setSession({
              ...makeSession('PLATFORM_ADMIN', 'firm-b'),
              token: 'replacement-token',
            })
          }
        >
          Replace operator session
        </button>
      );
    }
    renderWithProviders(
      <PluginsProvider modules={modules}>
        <App />
        <Switch />
      </PluginsProvider>,
      { session: makeSession('PLATFORM_ADMIN'), route: '/platform' },
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Request content access' }));
    await userEvent.type(screen.getByLabelText('Reason reference'), 'OLD-123');
    fireEvent.click(screen.getByRole('button', { name: 'Replace operator session' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByText('OLD-123')).not.toBeInTheDocument();
    await act(async () =>
      releaseRegistration(jsonResponse({ plugins: [{ name: 'platform-admin' }] })),
    );
    const banner = screen.getByRole('complementary', {
      name: 'Platform operator access',
    });
    expect(within(banner).getByRole('status')).toHaveTextContent('Checking content access');
    await act(async () =>
      releaseGrant(jsonResponse({ grant: { ...grant, reason: 'NEW-456' }, active: true })),
    );
    await waitFor(() => expect(banner).toHaveTextContent('NEW-456'));
    expect(banner).not.toHaveTextContent('OLD-123');
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
  });
});
