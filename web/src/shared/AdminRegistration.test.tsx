import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { definePlugin, useAuth } from '../plugins/api';
import { PluginsProvider } from '../plugins/PluginsProvider';
import { jsonResponse, makeSession, renderWithProviders } from '../test';
import { AdminDashboard } from './AdminDashboard';

const shell = definePlugin({
  name: 'fixture-admin',
  routes: [{ path: 'admin', admin: true, element: <AdminDashboard platform={false} /> }],
});
const section = definePlugin({
  name: 'fixture-section',
  nav: [{ to: '/admin?section=review', label: 'Review link' }],
  tenantAdminSections: [{ id: 'review', label: 'Review', render: () => <p>Review body</p> }],
});
const metadata = {
  name: 'fixture-section',
  description: 'Review',
  scope: 'tenant',
  health: 'ok',
  private: false,
  allowed: true,
  enabled: false,
  environments: ['dev'],
};
const modules = {
  'fixture-admin': async () => ({ default: shell }),
  'fixture-section': async () => ({ default: section }),
};

describe('Administration with real registration', () => {
  it('refreshes the displayed audit after an allowance write', async () => {
    let allowed = false;
    const platform = definePlugin({
      name: 'fixture-platform',
      routes: [{ path: 'platform', platformAdmin: true, element: <AdminDashboard platform /> }],
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (path: string, init?: RequestInit) => {
        if (path === '/api/plugins') return jsonResponse({ plugins: [{ name: platform.name }] });
        if (path === '/api/platform/plugins') return jsonResponse([{ ...metadata, allowed }]);
        if (path === '/api/platform/plugins/fixture-section') {
          allowed = JSON.parse(String(init?.body)).allowed;
          return jsonResponse({ ...metadata, allowed });
        }
        if (path === '/api/platform/plugin-audit')
          return jsonResponse({
            actions: allowed
              ? [
                  {
                    id: 1,
                    at: '2026-10-09T00:00:00Z',
                    actor: 'operator',
                    action: 'plugin.allowance',
                    arguments: 'plugin=fixture-section,allowed=True',
                    outcome: 'changed',
                  },
                ]
              : [],
            nextCursor: null,
          });
        return jsonResponse({}, 404);
      }),
    );
    renderWithProviders(
      <PluginsProvider modules={{ [platform.name]: async () => ({ default: platform }) }}>
        <App />
      </PluginsProvider>,
      { session: makeSession('PLATFORM_ADMIN'), route: '/platform' },
    );
    const input = await screen.findByRole('checkbox', { name: 'fixture-section' });
    expect(await screen.findByText('No permission changes recorded.')).toBeInTheDocument();
    await userEvent.click(input);
    expect(await screen.findByText('plugin=fixture-section,allowed=True')).toBeInTheDocument();
  });
  it('a permission write refreshes the real registration and drops a disabled contribution', async () => {
    let enabled = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (path: string, init?: RequestInit) => {
        if (path === '/api/admin/plugins/fixture-section') {
          enabled = JSON.parse(String(init?.body)).enabled;
          return jsonResponse({ ...metadata, enabled });
        }
        if (path === '/api/admin/plugins') return jsonResponse([{ ...metadata, enabled }]);
        if (path === '/api/admin/operator-audit')
          return jsonResponse({ actions: [], nextCursor: null });
        if (path === '/api/admin/content-access')
          return jsonResponse({ grants: [], nextCursor: null });
        if (path === '/api/plugins')
          return jsonResponse({
            plugins: [{ name: shell.name }, ...(enabled ? [{ name: section.name }] : [])],
          });
        return jsonResponse({ turns: 0, activeUsers: 0, domains: {} });
      }),
    );
    renderWithProviders(
      <PluginsProvider modules={modules}>
        <App />
      </PluginsProvider>,
      { session: makeSession('TENANT_ADMIN'), route: '/admin' },
    );
    const user = userEvent.setup();
    const input = await screen.findByRole('checkbox', { name: 'fixture-section' });
    expect(screen.queryByRole('link', { name: 'Review link' })).not.toBeInTheDocument();
    await user.click(input);
    expect(await screen.findByRole('link', { name: 'Review link' })).toBeInTheDocument();
    expect(screen.getByText('Review body')).toBeInTheDocument();
    await user.click(input);
    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    await waitFor(() =>
      expect(screen.queryByRole('link', { name: 'Review link' })).not.toBeInTheDocument(),
    );
    expect(screen.queryByText('Review body')).not.toBeInTheDocument();
  });

  it('removes the old organization sections while a new registration request is still pending', async () => {
    let answerB!: (response: Response) => void;
    const pendingB = new Promise<Response>((resolve) => {
      answerB = resolve;
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (path: string, init?: RequestInit) => {
        if (path === '/api/plugins') {
          if (
            init?.headers &&
            (init.headers as Record<string, string>).Authorization === 'Bearer token-b'
          )
            return pendingB;
          return jsonResponse({ plugins: [{ name: shell.name }, { name: section.name }] });
        }
        return jsonResponse([]);
      }),
    );
    function Switch() {
      const { setSession } = useAuth();
      return (
        <button
          onClick={() => setSession({ ...makeSession('TENANT_ADMIN', 'firm-b'), token: 'token-b' })}
        >
          Switch organization
        </button>
      );
    }
    renderWithProviders(
      <PluginsProvider modules={modules}>
        <App />
        <Switch />
      </PluginsProvider>,
      { session: makeSession('TENANT_ADMIN'), route: '/admin?section=review' },
    );
    expect(await screen.findByText('Review body')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Switch organization' }));
    expect(screen.queryByRole('link', { name: 'Review link' })).not.toBeInTheDocument();
    expect(screen.queryByText('Review body')).not.toBeInTheDocument();
    answerB(jsonResponse({ plugins: [{ name: shell.name }] }));
    expect(
      await screen.findByText('This section is not available for your organization.'),
    ).toBeInTheDocument();
  });

  it('a stale module load cannot register a previous organizations contribution', async () => {
    let release!: () => void;
    const pending = new Promise<void>((resolve) => {
      release = resolve;
    });
    const entered = vi.fn();
    const slow = {
      ...modules,
      'fixture-section': async () => {
        entered();
        await pending;
        return { default: section };
      },
    };
    let organizationB = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (path: string) =>
        path === '/api/plugins'
          ? jsonResponse({
              plugins: organizationB
                ? [{ name: shell.name }]
                : [{ name: shell.name }, { name: section.name }],
            })
          : jsonResponse([]),
      ),
    );
    function Switch() {
      const { setSession } = useAuth();
      return (
        <button
          onClick={() => {
            organizationB = true;
            setSession({ ...makeSession('TENANT_ADMIN', 'firm-b'), token: 'token-b' });
          }}
        >
          Switch organization
        </button>
      );
    }
    renderWithProviders(
      <PluginsProvider modules={slow}>
        <App />
        <Switch />
      </PluginsProvider>,
      { session: makeSession('TENANT_ADMIN'), route: '/admin?section=review' },
    );
    await waitFor(() => expect(entered).toHaveBeenCalled());
    fireEvent.click(screen.getByRole('button', { name: 'Switch organization' }));
    expect(
      await screen.findByText('This section is not available for your organization.'),
    ).toBeInTheDocument();
    release();
    await waitFor(() => expect(screen.queryByText('Review body')).not.toBeInTheDocument());
    expect(screen.queryByRole('link', { name: 'Review link' })).not.toBeInTheDocument();
  });

  it('a throwing contributor callback leaves another section and the shell usable', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const broken = definePlugin({
      name: 'fixture-broken',
      tenantAdminSections: [
        {
          id: 'broken',
          label: 'Broken',
          render: () => {
            throw new Error('secret');
          },
        },
      ],
    });
    const withBroken = { ...modules, 'fixture-broken': async () => ({ default: broken }) };
    vi.stubGlobal(
      'fetch',
      vi.fn(async (path: string) =>
        path === '/api/plugins'
          ? jsonResponse({ plugins: [shell, section, broken].map(({ name }) => ({ name })) })
          : path === '/api/admin/plugins'
            ? jsonResponse([])
            : path === '/api/admin/operator-audit'
              ? jsonResponse({ actions: [], nextCursor: null })
              : path === '/api/admin/content-access'
                ? jsonResponse({ grants: [], nextCursor: null })
                : jsonResponse({ turns: 0, activeUsers: 0, domains: {} }),
      ),
    );
    renderWithProviders(
      <PluginsProvider modules={withBroken}>
        <App />
      </PluginsProvider>,
      { session: makeSession('TENANT_ADMIN'), route: '/admin' },
    );
    expect(await screen.findByText('Review body')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Tenant administration' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Overview' })).toBeInTheDocument();
    expect(screen.queryByText('secret')).not.toBeInTheDocument();
  });
});
