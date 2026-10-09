import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { useAuth } from '@maf/plugin-api';
import { App, PluginsProvider, jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import tenantAdmin from './index';

describe('Tenant admin operator entries with real registration', () => {
  it('the installed tenant-admin module shows core operator entries with Compliance absent', async () => {
    const fetchMock = vi.fn(async (path: string) => {
      if (path === '/api/plugins') return jsonResponse({ plugins: [{ name: 'tenant-admin' }] });
      if (path === '/api/admin/plugins') return jsonResponse([]);
      if (path === '/api/admin/content-access')
        return jsonResponse({ grants: [], nextCursor: null });
      if (path === '/api/admin/usage')
        return jsonResponse({ turns: 0, activeUsers: 0, domains: {} });
      if (path === '/api/admin/operator-audit')
        return jsonResponse({
          actions: [
            {
              id: 1,
              at: '2026-10-09T12:00:00Z',
              operatorId: 'company-operator',
              rawSid: 'private-session',
              content: 'private-content',
            },
          ],
          nextCursor: null,
        });
      return jsonResponse({}, 404);
    });
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(
      <PluginsProvider modules={{ 'tenant-admin': async () => ({ default: tenantAdmin }) }}>
        <App />
      </PluginsProvider>,
      { session: makeSession('TENANT_ADMIN'), route: '/admin' },
    );
    expect(await screen.findByText('company-operator')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Operator entries' })).toBeInTheDocument();
    expect(screen.queryByText('private-session')).not.toBeInTheDocument();
    expect(screen.queryByText('private-content')).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([path]) => path.includes('compliance'))).toBe(false);
    expect(
      fetchMock.mock.calls
        .filter(([path]) => path.startsWith('/api/admin/operator-audit'))
        .map(([path]) => path),
    ).toEqual(['/api/admin/operator-audit']);
  });

  it.each(['USER', 'READ_ONLY', 'PLATFORM_ADMIN'] as const)(
    'the installed module denies %s before any entry read',
    async (role) => {
      const fetchMock = vi.fn<(path: string) => Promise<Response>>(async () =>
        jsonResponse({ plugins: [{ name: 'tenant-admin' }] }),
      );
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(
        <PluginsProvider modules={{ 'tenant-admin': async () => ({ default: tenantAdmin }) }}>
          <App />
        </PluginsProvider>,
        { session: makeSession(role), route: '/admin' },
      );
      expect(await screen.findByRole('alert')).toHaveTextContent(
        'This page requires the TENANT_ADMIN role',
      );
      expect(screen.queryByRole('region', { name: 'Operator entries' })).not.toBeInTheDocument();
      expect(fetchMock.mock.calls.some(([path]) => path.startsWith('/api/admin'))).toBe(false);
      expect(fetchMock).toHaveBeenCalledWith('/api/plugins', expect.anything());
    },
  );

  it('real registration removes the old tenant entry page and resets the cursor before reading the new tenant', async () => {
    let releaseRegistration!: (response: Response) => void;
    let releaseAudit!: (response: Response) => void;
    const registration = new Promise<Response>((resolve) => {
      releaseRegistration = resolve;
    });
    const audit = new Promise<Response>((resolve) => {
      releaseAudit = resolve;
    });
    const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
      const switched = (init?.headers as Record<string, string>).Authorization === 'Bearer token-b';
      if (path === '/api/plugins') {
        return switched ? registration : jsonResponse({ plugins: [{ name: 'tenant-admin' }] });
      }
      if (path === '/api/admin/content-access')
        return jsonResponse({ grants: [], nextCursor: null });
      if (path.startsWith('/api/admin/operator-audit')) {
        return switched
          ? audit
          : jsonResponse({
              actions: [
                {
                  id: 100,
                  at: '2026-10-09T12:00:00Z',
                  operatorId: path.includes('?') ? 'old-tenant-page' : 'old-tenant-latest',
                },
              ],
              nextCursor: 51,
            });
      }
      return path === '/api/admin/plugins'
        ? jsonResponse([])
        : jsonResponse({ turns: 0, activeUsers: 0, domains: {} });
    });
    vi.stubGlobal('fetch', fetchMock);
    function Switch() {
      const { setSession } = useAuth();
      return (
        <button
          onClick={() =>
            setSession({
              ...makeSession('TENANT_ADMIN', 'firm-b'),
              token: 'token-b',
            })
          }
        >
          Switch tenant
        </button>
      );
    }
    renderWithProviders(
      <PluginsProvider modules={{ 'tenant-admin': async () => ({ default: tenantAdmin }) }}>
        <App />
        <Switch />
      </PluginsProvider>,
      { session: makeSession('TENANT_ADMIN'), route: '/admin' },
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Earlier entries' }));
    expect(await screen.findByText('old-tenant-page')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Switch tenant' }));
    expect(screen.queryByText('old-tenant-page')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Operator entries' })).not.toBeInTheDocument();
    releaseRegistration(jsonResponse({ plugins: [{ name: 'tenant-admin' }] }));
    expect(await screen.findByRole('region', { name: 'Operator entries' })).toHaveTextContent(
      'Loading operator entries',
    );
    expect(screen.queryByRole('button', { name: 'Latest entries' })).not.toBeInTheDocument();
    await waitFor(() =>
      expect(
        fetchMock.mock.calls
          .filter(
            ([path, init]) =>
              path.startsWith('/api/admin/operator-audit') &&
              (init?.headers as Record<string, string>).Authorization === 'Bearer token-b',
          )
          .map(([path]) => path),
      ).toEqual(['/api/admin/operator-audit']),
    );
    releaseAudit(
      jsonResponse({
        actions: [
          {
            id: 1,
            at: '2026-10-09T12:00:00Z',
            operatorId: 'new-tenant-operator',
          },
        ],
        nextCursor: null,
      }),
    );
    expect(await screen.findByText('new-tenant-operator')).toBeInTheDocument();
    expect(screen.queryByText('old-tenant-latest')).not.toBeInTheDocument();
    expect(screen.getByText('Organization: firm-b')).toBeInTheDocument();
  });
});
