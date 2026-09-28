import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { EvalReportSummary, IntentStatsReport } from '../api/types';
import { App } from '../App';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { IntentStatsPage } from './IntentStatsPage';

const bins = (fill: Record<number, [number, number]>) =>
  Array.from({ length: 20 }, (_, i) => ({
    from: i / 20,
    to: (i + 1) / 20,
    used: fill[i]?.[0] ?? 0,
    gated: fill[i]?.[1] ?? 0,
  }));

const report = (overrides: Partial<IntentStatsReport> = {}): IntentStatsReport => ({
  window: '24h',
  from: '2026-09-27T12:00:00Z',
  to: '2026-09-28T12:00:00Z',
  bucketMinutes: 60,
  settings: { model: 'jev-1.13.0', minConfidence: 0.5, minInDomain: 0.2, timeoutSeconds: 2 },
  totals: { classified: 10, used: 6, gated: 2, failed: 2, forced: 4, excludedEvents: 3 },
  pipeline: {
    classified: 10,
    failed: 2,
    answered: 8,
    unknownChoice: 0,
    belowConfidence: 1,
    notForcingIntent: 2,
    forcingIntent: 5,
    outsideDomain: 1,
    forced: 4,
  },
  reasons: [
    { outcome: 'used', label: 'used', count: 6 },
    { outcome: 'gated', label: 'low confidence', count: 1 },
    { outcome: 'gated', label: 'outside the domain', count: 1 },
    { outcome: 'failed', label: 'timed out', count: 1 },
    { outcome: 'failed', label: 'rejected (503)', count: 1 },
  ],
  choices: [
    { choice: 'procedural', intent: 'procedural', count: 4 },
    { choice: 'procedural', intent: 'other', count: 1 },
    { choice: 'none', intent: 'other', count: 2 },
  ],
  timeline: Array.from({ length: 24 }, (_, i) => ({
    start: new Date(Date.UTC(2026, 8, 27, 12 + i)).toISOString(),
    used: i === 23 ? 6 : 0,
    gated: i === 23 ? 2 : 0,
    failed: i === 23 ? 2 : 0,
    timedOut: i === 23 ? 1 : 0,
    p50Ms: i === 23 ? 280 : null,
    p90Ms: i === 23 ? 2003 : null,
  })),
  confidence: bins({ 19: [6, 0], 8: [0, 1] }),
  inDomain: bins({ 19: [6, 0], 0: [0, 1] }),
  points: [
    { confidence: 0.98, inDomain: 0.97, outcome: 'used', choice: 'procedural' },
    { confidence: 0.95, inDomain: 0.03, outcome: 'gated', choice: 'procedural' },
  ],
  meanProbabilities: [
    { intent: 'procedural', mean: 0.61, chosen: 5 },
    { intent: 'mixed', mean: 0.05, chosen: 0 },
    { intent: 'data', mean: 0.12, chosen: 1 },
    { intent: 'chitchat', mean: 0.1, chosen: 1 },
    { intent: 'other', mean: 0.12, chosen: 1 },
  ],
  latency: {
    count: 9,
    p50: 280,
    p90: 2003,
    p99: 2003,
    max: 2003,
    bins: Array.from({ length: 21 }, (_, i) => ({
      fromMs: i * 100,
      toMs: i === 20 ? null : (i + 1) * 100,
      count: i === 2 ? 8 : i === 20 ? 1 : 0,
    })),
  },
  models: [{ model: 'jev-1.13.0', count: 10 }],
  ...overrides,
});

const evalRun = (runId: string, startedAt: string, holdout: number): EvalReportSummary => ({
  runId,
  suite: 'intent',
  startedAt,
  passed: true,
  variants: [
    {
      name: 'jev',
      metrics: {
        accuracy: 0.99,
        'accuracy:en': 1,
        'accuracy:bg': 1,
        'accuracy:bg-latn': 0.9643,
        'accuracy:design': 0.9859,
        'accuracy:holdout': holdout,
      },
      thresholds: { accuracy: 0.95 },
      passed: true,
      cases: 101,
      failures:
        runId === 'r2'
          ? [{ caseId: 'i-in-proc-bg-latn-02', reason: 'expected forced, got not forced' }]
          : [],
    },
  ],
});

function stub(stats: () => Response, reports: EvalReportSummary[] = []) {
  const urls: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      urls.push(url);
      return url.startsWith('/api/evals/reports') ? jsonResponse(reports) : stats();
    }),
  );
  return urls;
}

const admin = makeSession('FIRM_ADMIN');

describe('IntentStatsPage', () => {
  it('draws every chart with the floors the service runs with', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<IntentStatsPage />, { session: admin });

    expect(await screen.findByText('Classified turns')).toBeInTheDocument();
    expect(screen.getByText('40%')).toBeInTheDocument(); // forced 4 of 10
    expect(
      screen.getByText(/3 turn\(s\) in this period were classified by an earlier/),
    ).toBeInTheDocument();

    const pipeline = screen.getByRole('img', { name: 'Classification pipeline' });
    expect(pipeline).toHaveTextContent('confidence ≥ 0.5');
    expect(pipeline).toHaveTextContent('in-domain ≥ 0.2');
    expect(pipeline).toHaveTextContent('outside the domain');

    for (const name of [
      'Turns over time by outcome',
      'Latency over time',
      'Timeouts over time',
      'Outcomes by reason',
      'Mean probability per intent',
      'Confidence histogram',
      'In-domain histogram',
      'Confidence against in-domain probability',
      'Latency histogram',
      'Model versions',
    ]) {
      expect(screen.getByRole('img', { name })).toBeInTheDocument();
    }
    expect(screen.getByRole('img', { name: 'Confidence histogram' })).toHaveTextContent(
      'floor 0.5',
    );
    expect(screen.getByRole('img', { name: 'In-domain histogram' })).toHaveTextContent('floor 0.2');
    expect(screen.getByRole('img', { name: 'Latency histogram' })).toHaveTextContent('timeout 2s');
    expect(screen.getByRole('img', { name: 'Outcomes by reason' })).toHaveTextContent(
      'rejected (503)',
    );

    const matrix = screen.getByRole('table', { name: 'Choice against intent used' });
    expect(within(matrix).getByText('no answer')).toBeInTheDocument();
    expect(within(matrix).getByTitle('procedural → other: 1')).toBeInTheDocument();
  });

  it('shows the values behind a mark on hover', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<IntentStatsPage />, { session: admin });

    const reasons = await screen.findByRole('img', { name: 'Outcomes by reason' });
    const row = within(reasons).getByText('timed out').closest('g')!;
    await userEvent.hover(row);
    expect(await screen.findByRole('tooltip')).toHaveTextContent('failed: 1 (10%)');
  });

  it('says a period has no data instead of drawing empty charts', async () => {
    stub(() =>
      jsonResponse(
        report({
          totals: { classified: 0, used: 0, gated: 0, failed: 0, forced: 0, excludedEvents: 0 },
        }),
      ),
    );
    renderWithProviders(<IntentStatsPage />, { session: admin });

    expect(
      await screen.findByText(/No turn was classified by Jev in this period/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'Confidence histogram' })).not.toBeInTheDocument();
  });

  it('says the numbers could not be loaded and keeps its controls', async () => {
    stub(() => jsonResponse({ title: 'boom' }, 500));
    renderWithProviders(<IntentStatsPage />, { session: admin });

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not load the intent statistics',
    );
    expect(screen.getByLabelText('Period')).toBeInTheDocument();
  });

  it('asks the server for the period the user picked', async () => {
    const urls = stub(() => jsonResponse(report()));
    renderWithProviders(<IntentStatsPage />, { session: admin });
    await screen.findByText('Classified turns');

    await userEvent.selectOptions(screen.getByLabelText('Period'), '7d');
    await vi.waitFor(() => expect(urls).toContain('/api/admin/intent-stats?window=7d'));
    expect(urls.filter((u) => u.startsWith('/api/admin/'))[0]).toBe(
      '/api/admin/intent-stats?window=24h',
    );
  });

  it('draws the intent eval history and lists the latest failures', async () => {
    stub(
      () => jsonResponse(report()),
      [
        evalRun('r2', '2026-09-28T14:23:19Z', 1),
        evalRun('r1', '2026-09-28T11:59:36Z', 0.9667),
        { ...evalRun('x', '2026-09-28T12:00:00Z', 1), suite: 'selection' },
      ],
    );
    renderWithProviders(<IntentStatsPage />, { session: admin });

    const history = await screen.findByRole('region', { name: 'Intent eval history' });
    expect(
      await within(history).findByRole('img', { name: 'Intent eval accuracy per language' }),
    ).toBeInTheDocument();
    expect(
      within(history).getByRole('img', { name: 'Intent eval accuracy per split' }),
    ).toHaveTextContent('gate 0.95');
    const failures = within(history).getByRole('table', { name: 'Latest intent eval failures' });
    expect(failures).toHaveTextContent('i-in-proc-bg-latn-02');
  });
});

describe('/admin/intents', () => {
  it('is reached from the main navigation', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<App />, { route: '/admin/intents', session: admin });

    expect(await screen.findByRole('heading', { name: 'Jev intents' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Jev intents' })).toHaveAttribute(
      'href',
      '/admin/intents',
    );
  });

  it('denies an advisor, as the other admin screens do', () => {
    renderWithProviders(<App />, { route: '/admin/intents', session: makeSession('ADVISOR') });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
  });
});
