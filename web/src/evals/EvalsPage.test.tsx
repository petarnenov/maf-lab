import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { EvalReport, EvalReportSummary } from '../api/types';
import { jsonResponse, renderWithProviders } from '../test/render';
import { EvalsPage } from './EvalsPage';

const variants = [
  {
    name: 'hybrid',
    metrics: { recall_at_5: 0.82, mrr: 0.71 },
    thresholds: { recall_at_5: 0.7 },
    passed: true,
    cases: 40,
    failures: [],
  },
  {
    name: 'dense',
    metrics: { recall_at_5: 0.64, mrr: 0.55 },
    thresholds: { recall_at_5: 0.7 },
    passed: false,
    cases: 40,
    failures: [{ caseId: 'r-7', reason: 'relevant chunk not in top 5' }],
  },
];
const summary: EvalReportSummary = {
  runId: 'run-1',
  suite: 'retrieval',
  startedAt: '2026-09-19T10:00:00Z',
  passed: false,
  variants,
};
const report: EvalReport = {
  ...summary,
  finishedAt: '2026-09-19T10:05:00Z',
  settings: { rerank: 'off' },
};

describe('EvalsPage', () => {
  it('names what moved against the baseline, in words and figures', async () => {
    const withComparisons: EvalReport = {
      ...report,
      comparisons: [
        {
          variant: 'hybrid',
          metric: 'recall_at_5',
          baseline: 0.9,
          value: 0.82,
          delta: -0.08,
          status: 'Regression',
        },
        {
          variant: 'hybrid',
          metric: 'mrr',
          baseline: 0.65,
          value: 0.71,
          delta: 0.06,
          status: 'Improvement',
        },
        {
          variant: 'dense',
          metric: 'recall_at_5:bg',
          baseline: null,
          value: 0.5,
          delta: null,
          status: 'New',
        },
        {
          variant: 'dense',
          metric: 'mrr',
          baseline: 0.55,
          value: 0.545,
          delta: -0.005,
          status: 'Noise',
        },
      ],
    };
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url === '/api/evals/reports' ? jsonResponse([summary]) : jsonResponse(withComparisons),
      ),
    );
    renderWithProviders(<EvalsPage />);
    await userEvent.click(await screen.findByText('retrieval'));

    const list = await screen.findByRole('list', { name: 'Baseline comparison' });
    expect(list).toHaveTextContent('✗ regression · hybrid recall_at_5: 0.900 → 0.820 (-0.080)');
    expect(list).toHaveTextContent('↑ improved · hybrid mrr: 0.650 → 0.710 (+0.060)');
    expect(list).toHaveTextContent('+ new metric · dense recall_at_5:bg: 0.500, no baseline yet');
    // Noise is not worth a line.
    expect(list).not.toHaveTextContent('dense mrr');
  });

  it('shows no comparison for a report written before the gate existed', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) =>
        url === '/api/evals/reports' ? jsonResponse([summary]) : jsonResponse(report),
      ),
    );
    renderWithProviders(<EvalsPage />);
    await userEvent.click(await screen.findByText('retrieval'));

    await screen.findByRole('region', { name: 'Report detail' });
    expect(screen.queryByRole('list', { name: 'Baseline comparison' })).not.toBeInTheDocument();
  });

  it('lists runs with variants and metrics, and shows detail on click', async () => {
    const fetchMock = vi.fn(async (url: string) =>
      url === '/api/evals/reports' ? jsonResponse([summary]) : jsonResponse(report),
    );
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<EvalsPage />);

    const table = await screen.findByRole('table');
    expect(within(table).getByText('retrieval')).toBeInTheDocument();
    expect(within(table).getByText('hybrid')).toBeInTheDocument();
    expect(within(table).getByText('dense')).toBeInTheDocument();
    expect(within(table).getByText(/recall_at_5=0\.820/)).toBeInTheDocument();
    expect(within(table).getAllByText('fail')).toHaveLength(1);

    await userEvent.click(within(table).getByText('hybrid'));
    const detail = await screen.findByRole('region', { name: 'Report detail' });
    expect(within(detail).getByText(/rerank=off/)).toBeInTheDocument();
    expect(within(detail).getByText('1 failing cases')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith('/api/evals/reports/run-1', expect.anything());
  });

  it('shows an empty state', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderWithProviders(<EvalsPage />);
    expect(await screen.findByText(/No eval reports yet/)).toBeInTheDocument();
  });
});
