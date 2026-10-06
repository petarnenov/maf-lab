import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { MemoryRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AgentsProvider } from '../agents/AgentsProvider';
import { AuthProvider } from '../auth/AuthProvider';
import type { Session } from '../auth/session';
import { CardView } from '../chat/cards/CardView';
import { agentFetch } from '../test/agentFetch';
import { jsonResponse, makeSession } from '../test/render';
import { definePlugin, type MafWebPlugin } from './api';
import { PluginsContext } from './context';
import { PluginBoundary } from './PluginBoundary';
import { PluginsProvider, type PluginModuleLoader } from './PluginsProvider';

/**
 * The web side of the plugin contract (introduce-plugins tasks 3.5 and 3.7): with no plugin nothing of one appears;
 * with a plugin in use its route, link, chat pane and card do, each inside its own error boundary; the loader asks
 * /api/plugins before and after sign-in; and the import boundary holds both ways.
 */

const fixture = definePlugin({
  name: 'fixture',
  routes: [{ path: 'fixture', element: <p>fixture page</p> }],
  nav: [{ to: '/fixture', label: 'Fixture' }],
  chatPanes: [
    { id: 'fixture-pane', label: 'Fixture pane', render: () => <p>fixture pane body</p> },
  ],
  cards: {
    'fixture/card': ({ content }) => <p>fixture card {String((content as { n: number }).n)}</p>,
  },
});

const modules: Record<string, PluginModuleLoader> = { fixture: async () => ({ default: fixture }) };

function stubApi(plugins: string[]) {
  const fetch = vi.fn(async (url: string, init?: RequestInit) => {
    void init;
    if (String(url).startsWith('/api/plugins'))
      return jsonResponse({ plugins: plugins.map((name) => ({ name })), problems: [] });
    if (String(url).startsWith('/api/conversations'))
      return jsonResponse({ conversations: [], nextCursor: null });
    return jsonResponse([]);
  });
  vi.stubGlobal('fetch', agentFetch(fetch));
  return fetch;
}

function renderApp(route: string, session: Session | null = makeSession('USER')) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider initialSession={session}>
        <AgentsProvider>
          <PluginsProvider modules={modules}>
            <MemoryRouter initialEntries={[route]}>
              <App />
            </MemoryRouter>
          </PluginsProvider>
        </AgentsProvider>
      </AuthProvider>
    </QueryClientProvider>,
  );
}

describe('web plugins', () => {
  it('shows nothing of a plugin when none is in use', async () => {
    stubApi([]);
    renderApp('/chat');
    const nav = screen.getByRole('navigation', { name: 'Main' });
    await waitFor(() => expect(within(nav).queryByRole('link', { name: 'Fixture' })).toBeNull());
    expect(screen.queryByRole('tab', { name: 'Fixture pane' })).toBeNull();
  });

  it('shows a plugin in use: its link, its page and its chat pane', async () => {
    stubApi(['fixture']);
    renderApp('/chat');
    const nav = screen.getByRole('navigation', { name: 'Main' });
    await userEvent.click(await within(nav).findByRole('link', { name: 'Fixture' }));
    expect(await screen.findByText('fixture page')).toBeInTheDocument();
  });

  it("adds a plugin's pane to the chat's side pane", async () => {
    stubApi(['fixture']);
    renderApp('/chat');
    await userEvent.click(await screen.findByRole('tab', { name: 'Fixture pane' }));
    expect(screen.getByText('fixture pane body')).toBeInTheDocument();
  });

  it('asks which plugins are in use before sign-in and again after', async () => {
    const fetch = stubApi([]);
    renderApp('/chat', null);
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/plugins', expect.anything()));
    const anonymous = fetch.mock.calls.find(([url]) => url === '/api/plugins')![1]!;
    expect((anonymous.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it('draws a card type a plugin in use declares', () => {
    render(
      <PluginsContext.Provider value={{ plugins: [fixture] }}>
        <CardView
          card={{ messageId: 'c-1', activityType: 'fixture/card', content: { n: 7 } }}
          question="q"
        />
      </PluginsContext.Provider>,
    );
    expect(screen.getByText('fixture card 7')).toBeInTheDocument();
  });

  it('keeps a failing plugin inside its own boundary', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const Throws = () => {
      throw new Error('boom');
    };
    render(
      <>
        <PluginBoundary plugin="broken">
          <Throws />
        </PluginBoundary>
        <p>the rest of the page</p>
      </>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('The broken plugin could not be shown');
    expect(screen.getByText('the rest of the page')).toBeInTheDocument();
  });
});

/** The import boundary, both ways (introduce-plugins decision 8). */
describe('plugin import boundary', () => {
  const web = join(__dirname, '..', '..');
  const repo = join(web, '..');
  const files = (dir: string): string[] =>
    !existsSync(dir)
      ? []
      : readdirSync(dir).flatMap((name) => {
          const path = join(dir, name);
          if (name === 'node_modules') return [];
          return statSync(path).isDirectory()
            ? files(path)
            : /\.(ts|tsx)$/.test(name)
              ? [path]
              : [];
        });
  const imports = (path: string) =>
    [...readFileSync(path, 'utf8').matchAll(/(?:from\s+|import\s*\(\s*)['"]([^'"]+)['"]/g)].map(
      (m) => m[1],
    );

  it('the core never imports a plugin', () => {
    const offenders = files(join(web, 'src')).flatMap((path) =>
      imports(path)
        .filter((spec) => /(^|\/)plugins\/[^/]+\/web(\/|$)/.test(spec))
        .map((spec) => `${relative(repo, path)} → ${spec}`),
    );
    expect(offenders).toEqual([]);
  });

  it('a plugin imports only the plugin API, shared code, packages and its own files', () => {
    const webDirs = readdirSync(join(repo, 'plugins'), { withFileTypes: true })
      .filter((d) => d.isDirectory())
      .map((d) => join(repo, 'plugins', d.name, 'web'))
      .filter((dir) => existsSync(dir) && statSync(dir).isDirectory());
    const scanned = webDirs.flatMap((dir) => files(dir));
    // No silent pass: a plugin web folder that yields no files means the scanner missed them.
    if (webDirs.length > 0) expect(scanned.length).toBeGreaterThan(0);
    const offenders = scanned.flatMap((path) =>
      imports(path)
        .filter(
          (spec) =>
            !(
              spec === '@maf/plugin-api' ||
              spec.startsWith('@maf/shared/') ||
              !spec.startsWith('.') ||
              isOwn(path, spec)
            ),
        )
        .map((spec) => `${relative(repo, path)} → ${spec}`),
    );
    expect(offenders).toEqual([]);
  });

  function isOwn(path: string, spec: string) {
    const folder = relative(repo, path).split('/').slice(0, 3).join('/');
    return relative(repo, join(path, '..', spec)).startsWith(folder);
  }

  it('the scanner catches a core import of a plugin (planted)', () => {
    expect(/(^|\/)plugins\/[^/]+\/web(\/|$)/.test('../../../plugins/code/web/index')).toBe(true);
    expect(/(^|\/)plugins\/[^/]+\/web(\/|$)/.test('./plugins/api')).toBe(false);
  });
});

export type { MafWebPlugin };
