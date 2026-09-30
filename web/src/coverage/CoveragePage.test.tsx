import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { CoverageFileDetail, CoverageLine } from '../api/types';
import { App } from '../App';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { CoveragePage } from './CoveragePage';
import { sampleTree } from './treeModel.test';

export function detail(overrides: Partial<CoverageFileDetail> = {}): CoverageFileDetail {
  const lines: CoverageLine[] = [
    { line: 1, hits: 3, branchesCovered: 0, branchesTotal: 0, status: 'covered' },
    { line: 2, hits: 3, branchesCovered: 1, branchesTotal: 2, status: 'partial' },
    { line: 3, hits: 0, branchesCovered: 0, branchesTotal: 0, status: 'uncovered' },
  ];
  return {
    path: 'src/Lab/Beta.cs',
    toolchain: 'dotnet',
    commit: 'abcdef1234567890',
    measuredAt: '2026-09-30T10:00:00Z',
    dirty: false,
    kind: 'official',
    summary: {
      linesTotal: 3,
      linesCovered: 2,
      branchesTotal: 2,
      branchesCovered: 1,
      pct: 66.7,
      threshold: 80,
      thresholdIsOverride: false,
    },
    source: 'var a = 1;\nif (a > 0) b();\nreturn;\n// comment\n',
    lines,
    run: null,
    ...overrides,
  };
}

/** Answers the coverage API by path; anything else is a 404. */
export function stubCoverageApi(routes: Record<string, (method: string) => Response>) {
  const calls: { url: string; method: string; body?: string }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      calls.push({ url, method: init?.method ?? 'GET', body: typeof init?.body === 'string' ? init.body : undefined });
      const key = Object.keys(routes).find((k) => url.startsWith(k));
      return key ? routes[key](init?.method ?? 'GET') : jsonResponse({ title: 'Not found' }, 404);
    }),
  );
  return calls;
}

describe('CoveragePage', () => {
  it('is reached from the main navigation', async () => {
    stubCoverageApi({ '/api/coverage/tree': () => jsonResponse(sampleTree) });

    renderWithProviders(<App />, { route: '/coverage' });

    expect(await screen.findByRole('heading', { name: 'Coverage' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Coverage' })).toHaveAttribute('href', '/coverage');
  });

  it('shows the tree and flags files below threshold with more than colour', async () => {
    stubCoverageApi({ '/api/coverage/tree': () => jsonResponse(sampleTree) });

    renderWithProviders(<CoveragePage />);

    const beta = await screen.findByRole('button', { name: /Beta\.cs/ });
    expect(beta).toHaveTextContent('below');
    expect(beta).toHaveTextContent('40.0%');
    expect(screen.getByRole('button', { name: /Alpha\.cs/ })).not.toHaveTextContent('below');
  });

  it('opens a file with its summary and line statuses', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/files?path=src%2FLab%2FBeta.cs': () => jsonResponse(detail()),
    });

    renderWithProviders(<CoveragePage />);
    await userEvent.click(await screen.findByRole('button', { name: /Beta\.cs/ }));

    const view = await screen.findByRole('region', { name: 'File src/Lab/Beta.cs' });
    expect(view).toHaveTextContent('2 / 3');
    expect(view).toHaveTextContent('66.7%');
    expect(view).toHaveTextContent('80% (default)');
    expect(view).toHaveTextContent('abcdef1');
    const partial = view.querySelector('[data-line="2"]')!;
    expect(partial).toHaveAttribute('data-status', 'partial');
    expect(within(partial as HTMLElement).getByRole('tooltip')).toHaveTextContent('3 hits · 1/2 branches');
    expect(within(partial as HTMLElement).getByLabelText('partly covered')).toBeInTheDocument();
    // A comment is not executable: no status, no marker.
    expect(view.querySelector('[data-line="4"]')).not.toHaveAttribute('data-status');
  });

  it('renders a 5 000-line file as about a screenful of lines', async () => {
    const source = Array.from({ length: 5000 }, (_, i) => `line ${i + 1}`).join('\n');
    const lines: CoverageLine[] = Array.from({ length: 5000 }, (_, i) => ({
      line: i + 1,
      hits: i % 2,
      branchesCovered: 0,
      branchesTotal: 0,
      status: i % 2 ? 'covered' : 'uncovered',
    }));
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/files': () => jsonResponse(detail({ source, lines })),
    });

    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    const code = await screen.findByRole('region', { name: 'Source' });
    const rendered = () => code.querySelectorAll('[data-line]').length;
    expect(rendered()).toBeGreaterThan(0);
    expect(rendered()).toBeLessThanOrEqual(100);

    fireEvent.scroll(code, { target: { scrollTop: 20 * 4000 } });
    expect(code.querySelector('[data-line="4010"]')).not.toBeNull();
    expect(code.querySelector('[data-line="1"]')).toBeNull();
    expect(rendered()).toBeLessThanOrEqual(100);
  });

  it('says there is no report yet, and offers a refresh to an admin', async () => {
    const calls = stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse({ ...sampleTree, hasSnapshot: false, files: [], folders: [] }),
      '/api/coverage/refresh': (method) =>
        method === 'POST'
          ? jsonResponse({ jobId: 'j1', kind: 'coverage.refresh', state: 'running', startedAt: '', finishedAt: null, summary: null })
          : jsonResponse(undefined, 204),
    });

    renderWithProviders(<CoveragePage />, { session: makeSession('FIRM_ADMIN') });

    expect(await screen.findByText('No coverage report yet.')).toBeInTheDocument();
    await userEvent.click(await screen.findByRole('button', { name: 'Refresh coverage' }));
    expect(calls.some((c) => c.url === '/api/coverage/refresh' && c.method === 'POST')).toBe(true);
    // One refresh at a time: the button says so while it runs.
    expect(await screen.findByRole('button', { name: 'Measuring…' })).toBeDisabled();
  });

  it('does not offer a refresh to someone who is not an admin', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse({ ...sampleTree, hasSnapshot: false, files: [], folders: [] }),
    });

    renderWithProviders(<CoveragePage />);

    expect(await screen.findByText('No coverage report yet.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Refresh coverage' })).toBeNull();
  });

  it('shows a loading state while the tree is on its way', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));

    renderWithProviders(<CoveragePage />);

    expect(screen.getByText('Loading coverage…')).toBeInTheDocument();
  });

  const unavailable = () =>
    jsonResponse(
      {
        type: 'source_unavailable',
        title: 'Source unavailable',
        status: 409,
        detail:
          'This file was measured at commit 041553f5c412, which this repository does not have. Refresh coverage to measure it again.',
        commit: '041553f5c412f9db52cd6a8374d7f9b9b5e16dac',
      },
      409,
    );

  it('says a file was measured at a commit the repository lacks, and offers an admin a refresh there', async () => {
    const calls = stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/files': unavailable,
      '/api/coverage/refresh': (method) =>
        method === 'POST'
          ? jsonResponse({ jobId: 'j1', kind: 'coverage.refresh', state: 'running', startedAt: '', finishedAt: null, summary: null })
          : jsonResponse(undefined, 204),
    });

    renderWithProviders(<CoveragePage />, {
      route: '/coverage?file=src%2FLab%2FBeta.cs',
      session: makeSession('FIRM_ADMIN'),
    });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('measured at commit 041553f5c412, which this repository does not have');
    expect(screen.queryByText('Could not load this file.')).toBeNull();
    await userEvent.click(within(alert).getByRole('button', { name: 'Refresh coverage' }));
    expect(calls.some((c) => c.url === '/api/coverage/refresh' && c.method === 'POST')).toBe(true);
  });

  it('explains an unavailable commit to someone who is not an admin, without a refresh', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/files': unavailable,
    });

    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('which this repository does not have');
    expect(within(alert).queryByRole('button', { name: 'Refresh coverage' })).toBeNull();
  });

  const refreshJob = (state: string, summary: string | null) => ({
    jobId: 'j1',
    kind: 'coverage.refresh',
    state,
    startedAt: '2026-09-30T12:15:56Z',
    finishedAt: '2026-09-30T12:16:00Z',
    summary,
  });

  it.each([
    ['interrupted', 'The job was interrupted (its server instance stopped); start it again.'],
    ['runner down', 'The coverage runner could not be reached.'],
  ])('says when and why the last refresh did not succeed (%s)', async (_case, summary) => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/refresh': () => jsonResponse(refreshJob('failed', summary)),
    });

    renderWithProviders(<CoveragePage />, { session: makeSession('FIRM_ADMIN') });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(`The last refresh failed at ${new Date('2026-09-30T12:16:00Z').toLocaleString()}: ${summary}`);
    expect(alert).toHaveTextContent(`Current coverage was measured at ${new Date('2026-09-30T10:00:00Z').toLocaleString()}.`);
    expect(alert).not.toHaveTextContent('/app-data');
  });

  it('shows no refresh failure once a later refresh succeeded', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/refresh': () => jsonResponse(refreshJob('succeeded', '24e09c9 — dotnet: 237 files')),
    });

    renderWithProviders(<CoveragePage />, { session: makeSession('FIRM_ADMIN') });

    expect(await screen.findByRole('button', { name: 'Refresh coverage' })).toBeInTheDocument();
    expect(screen.queryByText(/The last refresh failed/)).toBeNull();
  });

  it('keeps a failing file view to itself, without internal detail', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse(sampleTree),
      '/api/coverage/files': () =>
        jsonResponse({ detail: 'SqliteException at /app-data/maf-lab.db', title: 'boom' }, 500),
    });

    renderWithProviders(<CoveragePage />, { route: '/coverage?file=src%2FLab%2FBeta.cs' });

    expect(await screen.findByText('Could not load this file.')).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent('SqliteException');
    // The tree is still there and still works.
    await userEvent.click(screen.getByRole('button', { name: /Alpha\.cs/ }));
    expect(screen.getByRole('button', { name: /Alpha\.cs/ })).toHaveAttribute('aria-current', 'true');
  });

  it('shows a tree error without internal detail', async () => {
    stubCoverageApi({
      '/api/coverage/tree': () => jsonResponse({ detail: 'System.InvalidOperationException in GitRepository' }, 500),
    });

    renderWithProviders(<CoveragePage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load coverage.');
    expect(document.body).not.toHaveTextContent('InvalidOperationException');
  });
});
