import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';

describe('main navigation', () => {
  it('links to the three inspectors after Curriculum, in a new tab on the page host', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderWithProviders(<App />, { session: makeSession('ADVISOR'), route: '/curriculum' });

    const nav = screen.getByRole('navigation', { name: 'Main' });
    const labels = Array.from(nav.querySelectorAll('a')).map(
      (a) => a.getAttribute('aria-label') ?? a.textContent,
    );
    expect(labels.slice(-4)).toEqual([
      'Curriculum',
      'A2A Inspector (opens in a new tab)',
      'MCP Inspector (opens in a new tab)',
      'Redis Insight (opens in a new tab)',
    ]);

    const host = `${window.location.protocol}//${window.location.hostname}`;
    for (const [name, port] of [
      ['A2A Inspector', 7172],
      ['MCP Inspector', 7173],
      ['Redis Insight', 7174],
    ] as const) {
      const link = screen.getByRole('link', { name: `${name} (opens in a new tab)` });
      expect(link).toHaveAttribute('href', `${host}:${port}`);
      expect(link).toHaveAttribute('target', '_blank');
      expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    }
  });
});
