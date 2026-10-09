import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { definePlugin } from '../plugins/api';
import { jsonResponse, makeSession, renderPluginApp } from '../test';
import { AdminDashboard } from './AdminDashboard';

const tenantShell = definePlugin({
  name: 'fixture-admin',
  routes: [{ path: 'admin', admin: true, element: <AdminDashboard platform={false} /> }],
});
const platformShell = definePlugin({
  name: 'fixture-platform',
  routes: [{ path: 'platform', platformAdmin: true, element: <AdminDashboard platform /> }],
  nav: [{ to: '/platform', label: 'Platform', platformAdminOnly: true }],
});
const contributor = definePlugin({
  name: 'fixture-review',
  tenantAdminSections: [
    { id: 'review', label: 'Review queue', render: () => <p>Review content</p> },
  ],
  platformAdminSections: [{ id: 'health', label: 'Health', render: () => <p>Health content</p> }],
});

describe('Administration section registries', () => {
  it.each([
    { plugins: [tenantShell], role: 'TENANT_ADMIN' as const, route: '/admin?section=review' },
    { plugins: [platformShell], role: 'PLATFORM_ADMIN' as const, route: '/platform' },
  ])('does not read operator entries on $route', async ({ plugins, role, route }) => {
    const fetchMock = vi.fn(async (path: string) =>
      path === '/api/platform/plugin-audit'
        ? jsonResponse({ actions: [], nextCursor: null })
        : jsonResponse([]),
    );
    vi.stubGlobal('fetch', fetchMock);
    renderPluginApp(plugins, { session: makeSession(role), route });
    expect(
      await screen.findByRole('heading', {
        name: role === 'TENANT_ADMIN' ? 'Tenant administration' : 'Platform administration',
      }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Operator entries' })).not.toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([path]) => path.startsWith('/api/admin/operator-audit')),
    ).toBe(false);
  });

  it('renders only the in-use contributor for the right dashboard', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderPluginApp([tenantShell, contributor], {
      session: makeSession('TENANT_ADMIN'),
      route: '/admin?section=review',
    });
    expect(await screen.findByText('Review content')).toBeInTheDocument();
    expect(screen.queryByText('Health content')).not.toBeInTheDocument();
  });
  it('removed contributions cannot be opened via a saved URL', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderPluginApp([tenantShell], {
      session: makeSession('TENANT_ADMIN'),
      route: '/admin?section=review',
    });
    expect(
      await screen.findByText('This section is not available for your organization.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Review content')).not.toBeInTheDocument();
  });
  it('guards platform routes and navigation before fetching permissions', async () => {
    const fetchMock = vi.fn(async (path: string) => {
      void path;
      return jsonResponse([]);
    });
    vi.stubGlobal('fetch', fetchMock);
    renderPluginApp([platformShell, contributor], {
      session: makeSession('TENANT_ADMIN'),
      route: '/platform',
    });
    expect(await screen.findByRole('alert')).toHaveTextContent('PLATFORM_ADMIN');
    expect(screen.queryByRole('link', { name: 'Platform' })).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([path]) => String(path).startsWith('/api/platform'))).toBe(
      false,
    );
  });
  it('allows an organization-scoped operator to open platform contributions', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderPluginApp([platformShell, contributor], {
      session: makeSession('PLATFORM_ADMIN', 'firm-b'),
      route: '/platform?section=health',
    });
    expect(await screen.findByText('Health content')).toBeInTheDocument();
    expect(screen.getByText('Organization: firm-b')).toBeInTheDocument();
    expect(screen.queryByText('Review content')).not.toBeInTheDocument();
  });
});
