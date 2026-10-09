import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderWithProviders, PluginsContext } from '@maf/testing';
import { definePlugin } from '@maf/plugin-api';
import { CURRICULUM } from './curriculum';
import { CurriculumPage } from './CurriculumPage';

describe('CurriculumPage', () => {
  afterEach(() => vi.restoreAllMocks());

  it('renders every day in order, then rules, then what is not covered', () => {
    renderWithProviders(<CurriculumPage />);

    const titles = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent);
    expect(titles).toEqual([...CURRICULUM.map((s) => s.title), 'Not covered']);
    expect(titles.at(-2)).toMatch(/rules/i);
  });

  it('renders with no persona and makes no request', () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    renderWithProviders(<CurriculumPage />, { session: null });

    expect(screen.getByRole('heading', { level: 1, name: 'Curriculum' })).toBeInTheDocument();
    expect(screen.getAllByRole('article').length).toBe(
      CURRICULUM.reduce((n, s) => n + s.entries.length, 0),
    );
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('shows the paths, the spec and a link to the screen', () => {
    renderWithProviders(<CurriculumPage />);
    const entry = CURRICULUM.flatMap((s) => s.entries).find((e) => e.screen)!;

    const card = screen.getByRole('heading', { level: 3, name: entry.concept }).closest('article')!;
    expect(within(card).getByText(entry.paths[0])).toBeInTheDocument();
    expect(within(card).getByText(`spec: ${entry.spec}`)).toBeInTheDocument();
    expect(
      within(card).getByRole('link', { name: new RegExp(entry.screen!.label) }),
    ).toHaveAttribute('href', entry.screen!.to);
  });
  it('links an optional screen only while its route is contributed', () => {
    const entry = CURRICULUM.flatMap((section) => section.entries).find(
      (entry) => entry.screen?.optional,
    )!;
    const label = new RegExp(`See it on ${entry.screen!.label}`);
    const { unmount } = renderWithProviders(<CurriculumPage />);
    expect(screen.queryAllByRole('link', { name: label })).toHaveLength(0);
    unmount();
    const page = definePlugin({
      name: 'fixture',
      routes: [{ path: entry.screen!.to.slice(1), element: <p>optional page</p> }],
    });
    renderWithProviders(
      <PluginsContext.Provider value={{ plugins: [page] }}>
        <CurriculumPage />
      </PluginsContext.Provider>,
    );
    expect(screen.getAllByRole('link', { name: label }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('link', { name: label })[0]).toHaveAttribute(
      'href',
      entry.screen!.to,
    );
  });
});
