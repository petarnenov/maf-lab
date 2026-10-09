import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { useAuth } from '@maf/plugin-api';
import { App, PluginsProvider, jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import tenantAdmin from './index';

const grant = (id: number, operatorId: string) => ({
  id,
  operatorId,
  reason: 'INC-123',
  startedAt: '2026-10-09T12:00:00Z',
  expiresAt: '2026-10-09T13:00:00Z',
  endRequestedAt: null,
  endedAt: null,
});
const modules = { 'tenant-admin': async () => ({ default: tenantAdmin }) };

describe('Tenant content access with actual module registration', () => {
  it('shows core content access metadata next to operator entries with Compliance absent', async () => {
    const fetchMock = vi.fn(async (path: string) => {
      if (path === '/api/plugins') return jsonResponse({ plugins: [{ name: 'tenant-admin' }] });
      if (path === '/api/admin/plugins') return jsonResponse([]);
      if (path === '/api/admin/operator-audit')
        return jsonResponse({ actions: [], nextCursor: null });
      if (path === '/api/admin/content-access')
        return jsonResponse({
          grants: [
            {
              ...grant(1, 'company-operator'),
              content: 'private-content',
              token: 'private-token',
            },
          ],
          nextCursor: null,
        });
      return jsonResponse({ turns: 0, activeUsers: 0, domains: {} });
    });
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(
      <PluginsProvider modules={modules}>
        <App />
      </PluginsProvider>,
      {
        session: makeSession('TENANT_ADMIN'),
        route: '/admin',
      },
    );
    expect(
      await screen.findByRole('region', {
        name: 'Operator access to your content',
      }),
    ).toBeInTheDocument();
    expect(await screen.findByText('company-operator')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Operator entries' })).toBeInTheDocument();
    expect(screen.queryByText('private-content')).not.toBeInTheDocument();
    expect(screen.queryByText('private-token')).not.toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        ([path]) => path.includes('compliance') || path.startsWith('/api/platform'),
      ),
    ).toBe(false);
  });

  it.each(['USER', 'READ_ONLY', 'PLATFORM_ADMIN'] as const)(
    '%s cannot read tenant content access via the real route',
    async (role) => {
      const fetchMock = vi.fn(async (path: string) =>
        path === '/api/plugins'
          ? jsonResponse({ plugins: [{ name: 'tenant-admin' }] })
          : jsonResponse({ grant: null, active: false }),
      );
      vi.stubGlobal('fetch', fetchMock);
      renderWithProviders(
        <PluginsProvider modules={modules}>
          <App />
        </PluginsProvider>,
        { session: makeSession(role), route: '/admin' },
      );
      expect(await screen.findByRole('alert')).toHaveTextContent(
        'This page requires the TENANT_ADMIN role',
      );
      expect(
        screen.queryByRole('region', {
          name: 'Operator access to your content',
        }),
      ).not.toBeInTheDocument();
      expect(
        fetchMock.mock.calls.some(([path]) => path.startsWith('/api/admin/content-access')),
      ).toBe(false);
    },
  );

  it('resets the metadata cursor after the new tenant registration, ignoring a late old page response', async () => {
    let releaseRegistration!: (response: Response) => void;
    let releaseOld!: (response: Response) => void;
    const registration = new Promise<Response>((resolve) => {
      releaseRegistration = resolve;
    });
    const old = new Promise<Response>((resolve) => {
      releaseOld = resolve;
    });
    const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
      const replacement =
        (init?.headers as Record<string, string>).Authorization === 'Bearer replacement-token';
      if (path === '/api/plugins')
        return replacement ? registration : jsonResponse({ plugins: [{ name: 'tenant-admin' }] });
      if (path.startsWith('/api/admin/content-access')) {
        if (replacement)
          return jsonResponse({
            grants: [grant(200, 'new-tenant-operator')],
            nextCursor: null,
          });
        return path.includes('?')
          ? old
          : jsonResponse({
              grants: [grant(100, 'old-tenant-operator')],
              nextCursor: 51,
            });
      }
      if (path === '/api/admin/plugins') return jsonResponse([]);
      if (path === '/api/admin/operator-audit')
        return jsonResponse({ actions: [], nextCursor: null });
      return jsonResponse({ turns: 0, activeUsers: 0, domains: {} });
    });
    vi.stubGlobal('fetch', fetchMock);
    function Switch() {
      const { setSession } = useAuth();
      return (
        <button
          onClick={() =>
            setSession({
              ...makeSession('TENANT_ADMIN', 'firm-b'),
              token: 'replacement-token',
            })
          }
        >
          Switch tenant
        </button>
      );
    }
    renderWithProviders(
      <PluginsProvider modules={modules}>
        <App />
        <Switch />
      </PluginsProvider>,
      { session: makeSession('TENANT_ADMIN'), route: '/admin' },
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Earlier content access' }));
    fireEvent.click(screen.getByRole('button', { name: 'Switch tenant' }));
    expect(screen.queryByText('old-tenant-operator')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: 'Operator access to your content' }),
    ).not.toBeInTheDocument();
    await act(async () =>
      releaseRegistration(jsonResponse({ plugins: [{ name: 'tenant-admin' }] })),
    );
    expect(await screen.findByText('new-tenant-operator')).toBeInTheDocument();
    await act(async () =>
      releaseOld(
        jsonResponse({
          grants: [grant(50, 'stale-operator')],
          nextCursor: null,
        }),
      ),
    );
    expect(screen.queryByText('stale-operator')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Latest content access' })).not.toBeInTheDocument();
    await waitFor(() =>
      expect(
        fetchMock.mock.calls
          .filter(
            ([path, init]) =>
              path.startsWith('/api/admin/content-access') &&
              (init?.headers as Record<string, string>).Authorization ===
                'Bearer replacement-token',
          )
          .map(([path]) => path),
      ).toEqual(['/api/admin/content-access']),
    );
  });
});
