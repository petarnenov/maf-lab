import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { EvalReportSummary, IntentStatsReport, JevStatsReport } from '../api/types';
import { App } from '../App';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { JevPage } from './JevPage';

const bins = (fill: Record<number, [number, number]>) =>
  Array.from({ length: 20 }, (_, i) => ({
    from: i / 20,
    to: (i + 1) / 20,
    used: fill[i]?.[0] ?? 0,
    gated: fill[i]?.[1] ?? 0,
  }));

const intent = (): IntentStatsReport => ({
  window: '24h',
  from: '2026-09-27T12:00:00Z',
  to: '2026-09-28T12:00:00Z',
  bucketMinutes: 60,
  settings: { model: 'jev-1.13.0', minConfidence: 0.5, minInDomain: 0.2, timeoutSeconds: 2 },
  totals: { classified: 10, used: 6, gated: 2, failed: 2, forced: 4, excludedEvents: 0 },
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
    { outcome: 'failed', label: 'timed out', count: 1 },
  ],
  choices: [
    { choice: 'procedural', intent: 'procedural', count: 4 },
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
  points: [{ confidence: 0.98, inDomain: 0.97, outcome: 'used', choice: 'procedural' }],
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
});

const latency = (count: number) => ({
  count,
  p50: 300,
  p90: 420,
  p99: 500,
  max: 500,
  bins: Array.from({ length: 21 }, (_, i) => ({
    fromMs: i * 100,
    toMs: i === 20 ? null : (i + 1) * 100,
    count: i === 3 ? count : 0,
  })),
});

const timeline24 = <T,>(make: (i: number) => T): T[] =>
  Array.from({ length: 24 }, (_, i) => make(i));

const report = (overrides: Partial<JevStatsReport> = {}): JevStatsReport => ({
  window: '24h',
  from: '2026-09-27T12:00:00Z',
  to: '2026-09-28T12:00:00Z',
  bucketMinutes: 60,
  overview: {
    settings: {
      model: 'jev-1.13.0',
      guardEnabled: true,
      promptBlockAt: 0.65,
      contentWithholdAt: 0.85,
      crossTenantAt: 0.8,
      relevanceFloor: 0.3,
    },
    requests: 40,
    unavailable: 3,
    turns: 10,
    requestsPerTurn: 4,
    sites: [
      { site: 'intent', requests: 10, unavailable: 1, p50Ms: 280, p90Ms: 2003 },
      { site: 'guardrail', requests: 22, unavailable: 1, p50Ms: 300, p90Ms: 420 },
      { site: 'relevance', requests: 8, unavailable: 1, p50Ms: 310, p90Ms: 430 },
    ],
    timeline: timeline24((i) => ({
      start: new Date(Date.UTC(2026, 8, 27, 12 + i)).toISOString(),
      requests: i === 23 ? 40 : 0,
      unavailable: i === 23 ? 3 : 0,
    })),
  },
  intent: intent(),
  guardrail: {
    checks: [
      { check: 'prompt', total: 10, pass: 8, blocked: 2, withheld: 0, unscreened: 0 },
      { check: 'tool_result', total: 12, pass: 10, blocked: 0, withheld: 1, unscreened: 1 },
    ],
    trippedBy: [
      { question: 'guard_override', decision: 'blocked', count: 2 },
      { question: 'guard_exfiltrate', decision: 'withheld', count: 1 },
    ],
    screened: 22,
    blocked: 2,
    withheld: 1,
    unscreened: 1,
    latency: latency(12),
    timeline: timeline24((i) => ({
      start: new Date(Date.UTC(2026, 8, 27, 12 + i)).toISOString(),
      screened: i === 23 ? 22 : 0,
      blocked: i === 23 ? 2 : 0,
      withheld: i === 23 ? 1 : 0,
      unscreened: i === 23 ? 1 : 0,
    })),
  },
  relevance: {
    floor: 0.3,
    searches: 8,
    gated: 2,
    reranked: 8,
    unavailable: 1,
    maxHistogram: Array.from({ length: 20 }, (_, i) => ({
      from: i / 20,
      to: (i + 1) / 20,
      kept: i === 16 ? 5 : 0,
      gated: i === 2 ? 2 : 0,
    })),
    latency: latency(8),
    timeline: timeline24((i) => ({
      start: new Date(Date.UTC(2026, 8, 27, 12 + i)).toISOString(),
      searches: i === 23 ? 8 : 0,
      gated: i === 23 ? 2 : 0,
      unavailable: i === 23 ? 1 : 0,
    })),
  },
  routing: {
    enabled: true,
    dataTurns: 5,
    routed: 3,
    tools: [
      { tool: 'get_billing_run_status', count: 2 },
      { tool: 'search_billing_runs', count: 1 },
    ],
    notRoutedReasons: [{ reason: 'a write is indicated', count: 2 }],
    modelCallsRoutedMedian: 1,
    modelCallsUnroutedMedian: 2,
    latency: latency(3),
  },
  ...overrides,
});

const evalRun = (runId: string, startedAt: string): EvalReportSummary => ({
  runId,
  suite: 'intent',
  startedAt,
  passed: true,
  variants: [
    {
      name: 'jev',
      metrics: { accuracy: 0.99, 'accuracy:en': 1, 'accuracy:design': 0.99, 'accuracy:holdout': 1 },
      thresholds: { accuracy: 0.95 },
      passed: true,
      cases: 101,
      failures: [],
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

describe('JevPage', () => {
  it('draws the overview and every call site with the running floors', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<JevPage />, { session: admin });

    // Overview
    expect(await screen.findByText('Jev requests')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Jev requests over time' })).toBeInTheDocument();
    const bySite = screen.getByRole('table', { name: 'Requests by call site' });
    expect(within(bySite).getByText('relevance')).toBeInTheDocument();

    // Intent (reused section)
    expect(screen.getByRole('img', { name: 'Confidence histogram' })).toHaveTextContent(
      'floor 0.5',
    );

    // Guardrail
    expect(screen.getByRole('img', { name: 'Screenings by check' })).toBeInTheDocument();
    // The screen says what it covers: chat turns, not the A2A partner path.
    expect(
      screen.getByText(/Jev calls made by chat turns — the A2A partner path is not counted/),
    ).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Guard questions that tripped' })).toHaveTextContent(
      'guard_override',
    );

    // Relevance
    expect(screen.getByRole('img', { name: 'Top relevance histogram' })).toHaveTextContent(
      'floor 0.3',
    );

    // Routing
    expect(screen.getByRole('img', { name: 'Routed tools' })).toHaveTextContent(
      'get_billing_run_status',
    );
    expect(screen.getByText('Model calls · routed')).toBeInTheDocument();
  });

  it("shows where Jev placed the turns among the domains and each domain's judged searches", async () => {
    const base = report();
    stub(() =>
      jsonResponse(
        report({
          relevance: {
            ...base.relevance,
            byDomain: [
              { domain: 'billing', searches: 7, gated: 1, unavailable: 0 },
              { domain: 'portfolio', searches: 3, gated: 2, unavailable: 1 },
            ],
          },
          domains: {
            judged: 10,
            billing: 5,
            portfolio: 2,
            both: 2,
            none: 1,
            crossed: 3,
            withCalls: 8,
            agreed: 6,
          },
        }),
      ),
    );
    renderWithProviders(<JevPage />, { session: admin });

    expect(await screen.findByText('Both (crossing)')).toBeInTheDocument();
    expect(
      screen.getByText("3 turns went from one server's tools to the other's"),
    ).toBeInTheDocument();
    expect(screen.getByText('6 of 8 turns that called a tool')).toBeInTheDocument();
    const perDomain = screen.getByLabelText('Judged searches per domain');
    expect(within(perDomain).getByText('portfolio documentation')).toBeInTheDocument();
    expect(within(perDomain).getByText('2 silenced · 1 ungated')).toBeInTheDocument();
  });

  it('counts the answer check as a site of its own and shows its section', async () => {
    const base = report();
    stub(() =>
      jsonResponse(
        report({
          overview: {
            ...base.overview,
            sites: [
              ...base.overview.sites,
              { site: 'answer', requests: 9, unavailable: 1, p50Ms: 420, p90Ms: 610 },
            ],
          },
          answerCheck: {
            answers: 10,
            checked: 8,
            pass: 5,
            notRelevant: 1,
            notGrounded: 2,
            unchecked: 2,
            unavailable: 1,
            relevantFloor: 0.5,
            groundedFloor: 0.5,
            latency: latency(9),
          },
        }),
      ),
    );
    renderWithProviders(<JevPage />, { session: admin });

    const bySite = await screen.findByRole('table', { name: 'Requests by call site' });
    const answerRow = within(bySite).getByText('answer').closest('tr')!;
    expect(answerRow).toHaveTextContent('9');
    expect(answerRow).toHaveTextContent('1 (11%)');

    expect(screen.getByRole('heading', { name: 'Answer check' })).toBeInTheDocument();
    expect(screen.getByText('Answers checked')).toBeInTheDocument();
    expect(screen.getByText('10 answered turns')).toBeInTheDocument();
    expect(screen.getByText('Not grounded').parentElement).toHaveTextContent('25%');
    expect(screen.getByText('Not relevant').parentElement).toHaveTextContent('13%');
    expect(screen.getByText('2 below floor 0.5')).toBeInTheDocument();
    expect(
      screen.getByText('1 Jev unavailable · the rest off, no key or over the cap'),
    ).toBeInTheDocument();
    // An api from before the review band sends no uncertain count: no tile for it.
    expect(screen.queryByText('Uncertain')).not.toBeInTheDocument();
    const latencyPanel = screen.getByRole('region', { name: 'Answer check latency' });
    expect(within(latencyPanel).getByRole('img', { name: 'Latency histogram' })).toHaveTextContent(
      'timeout 3s',
    );
  });

  it('shows the uncertain answers of the review band beside the flagged ones', async () => {
    stub(() =>
      jsonResponse(
        report({
          answerCheck: {
            answers: 5,
            checked: 4,
            pass: 2,
            uncertain: 1,
            notRelevant: 0,
            notGrounded: 1,
            unchecked: 1,
            unavailable: 0,
            relevantFloor: 0.2,
            groundedFloor: 0.2,
            latency: latency(4),
          },
        }),
      ),
    );
    renderWithProviders(<JevPage />, { session: admin });

    expect(await screen.findByText('Uncertain')).toBeInTheDocument();
    expect(screen.getByText('Uncertain').parentElement).toHaveTextContent('25%');
    expect(screen.getByText('1 in the review band · no signal')).toBeInTheDocument();
    expect(screen.getByText('1 below floor 0.2')).toBeInTheDocument();
  });

  it('shows the calls an open circuit skipped apart from the unavailable requests', async () => {
    const base = report();
    stub(() =>
      jsonResponse(
        report({
          overview: {
            ...base.overview,
            skipped: 12,
            sites: base.overview.sites.map((site) =>
              site.site === 'relevance' ? { ...site, skipped: 12 } : { ...site, skipped: 0 },
            ),
            timeline: base.overview.timeline.map((b, i) => ({ ...b, skipped: i === 23 ? 12 : 0 })),
          },
        }),
      ),
    );
    renderWithProviders(<JevPage />, { session: admin });

    expect((await screen.findByText('Skipped')).parentElement).toHaveTextContent('12');
    expect(screen.getByText('3 of 40')).toBeInTheDocument();
    const bySite = screen.getByRole('table', { name: 'Requests by call site' });
    const relevanceRow = within(bySite).getByText('relevance').closest('tr')!;
    expect(within(relevanceRow).getAllByRole('cell')[3]).toHaveTextContent('12');
    expect(screen.getAllByText('skipped (circuit open)').length).toBeGreaterThan(0);

    const chart = screen.getByRole('img', { name: 'Jev requests over time' });
    const buckets = chart.querySelectorAll('rect');
    await userEvent.hover(buckets[buckets.length - 1]);
    expect(await screen.findByRole('tooltip')).toHaveTextContent('skipped (circuit open): 12');
  });

  it('draws no skipped series when nothing was skipped', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<JevPage />, { session: admin });

    expect((await screen.findByText('Skipped')).parentElement).toHaveTextContent('0');
    expect(screen.getByRole('img', { name: 'Jev requests over time' })).toBeInTheDocument();
    expect(screen.queryByText('skipped (circuit open)')).not.toBeInTheDocument();
  });

  it('says when no answer was checked', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<JevPage />, { session: admin });

    expect(
      await screen.findByText(/No answer was checked by Jev in this period/),
    ).toBeInTheDocument();
  });

  it('shows the values behind a mark on hover', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<JevPage />, { session: admin });

    const tripped = await screen.findByRole('img', { name: 'Guard questions that tripped' });
    const row = within(tripped).getByText('guard_override').closest('g')!;
    await userEvent.hover(row);
    expect(await screen.findByRole('tooltip')).toHaveTextContent('blocked: 2');
  });

  it('says there is no data instead of drawing empty charts', async () => {
    stub(() =>
      jsonResponse(
        report({
          overview: { ...report().overview, requests: 0 },
          intent: { ...intent(), totals: { ...intent().totals, classified: 0 } },
        }),
      ),
    );
    renderWithProviders(<JevPage />, { session: admin });

    expect(await screen.findByText(/Jev made no request in this period/)).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'Screenings by check' })).not.toBeInTheDocument();
  });

  it('says the numbers could not be loaded and keeps its controls', async () => {
    stub(() => jsonResponse({ title: 'boom' }, 500));
    renderWithProviders(<JevPage />, { session: admin });

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the Jev statistics');
    expect(screen.getByLabelText('Period')).toBeInTheDocument();
  });

  it('asks the server for the period the user picked', async () => {
    const urls = stub(() => jsonResponse(report()));
    renderWithProviders(<JevPage />, { session: admin });
    await screen.findByText('Jev requests');

    await userEvent.selectOptions(screen.getByLabelText('Period'), '7d');
    await vi.waitFor(() => expect(urls).toContain('/api/admin/jev-stats?window=7d'));
    expect(urls.filter((u) => u.startsWith('/api/admin/'))[0]).toBe(
      '/api/admin/jev-stats?window=24h',
    );
  });

  it('draws the intent eval history', async () => {
    stub(() => jsonResponse(report()), [evalRun('r1', '2026-09-28T11:59:36Z')]);
    renderWithProviders(<JevPage />, { session: admin });

    const history = await screen.findByRole('region', { name: 'Intent eval history' });
    expect(
      await within(history).findByRole('img', { name: 'Intent eval accuracy per language' }),
    ).toBeInTheDocument();
  });
});

describe('/admin/jev', () => {
  it('is reached from the main navigation', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<App />, { route: '/admin/jev', session: admin });

    expect(await screen.findByRole('heading', { name: 'Jev', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Jev' })).toHaveAttribute('href', '/admin/jev');
  });

  it('keeps the old /admin/intents link working', async () => {
    stub(() => jsonResponse(report()));
    renderWithProviders(<App />, { route: '/admin/intents', session: admin });

    expect(await screen.findByRole('heading', { name: 'Jev', level: 1 })).toBeInTheDocument();
  });

  it('denies an advisor, as the other admin screens do', () => {
    renderWithProviders(<App />, { route: '/admin/jev', session: makeSession('ADVISOR') });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
  });
});
