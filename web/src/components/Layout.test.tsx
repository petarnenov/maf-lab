import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import type { MafWebPlugin } from '../plugins/api';
import { PluginsProvider, type PluginModuleLoader } from '../plugins/PluginsProvider';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';

/** Every plugin's web part in this build, by name — whatever plugin folders are present. */
const bundled = import.meta.glob<{ default: MafWebPlugin }>('../../../plugins/*/web/index.ts', {
  eager: true,
});
const plugins = Object.values(bundled).map((m) => m.default);
const modules: Record<string, PluginModuleLoader> = Object.fromEntries(
  plugins.map((p) => [p.name, async () => ({ default: p })]),
);

function render(installed: string[]) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) =>
      String(url).startsWith('/api/plugins')
        ? jsonResponse({ plugins: installed.map((name) => ({ name })), problems: [], domains: [] })
        : jsonResponse([]),
    ),
  );
  renderWithProviders(
    <PluginsProvider modules={modules}>
      <App />
    </PluginsProvider>,
    { session: makeSession('USER'), route: '/curriculum' },
  );
}

const links = () =>
  Array.from(screen.getByRole('navigation', { name: 'Main' }).querySelectorAll('a')).map(
    (a) => a.getAttribute('aria-label') ?? a.textContent,
  );

describe('main navigation', () => {
  it('links to no developer tool of its own: each one is a plugin (introduce-plugins 5o)', async () => {
    render([]);

    await waitFor(() => expect(links()).toContain('Curriculum'));
    expect(links().filter((l) => l?.includes('(opens in a new tab)'))).toEqual([]);
  });

  it('links to every installed plugin’s outside tool, in a new tab on the page host', async () => {
    const tools = plugins.flatMap((p) =>
      (p.nav ?? []).filter((n) => n.href).map((n) => ({ plugin: p.name, ...n })),
    );
    render(tools.map((t) => t.plugin));

    for (const tool of tools) {
      const link = await screen.findByRole('link', { name: `${tool.label} (opens in a new tab)` });
      expect(link).toHaveAttribute('href', tool.href);
      expect(link.getAttribute('href')).toMatch(
        new RegExp(`^${window.location.protocol}//${window.location.hostname}:\\d+$`),
      );
      expect(link).toHaveAttribute('target', '_blank');
      expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    }
  });
});
