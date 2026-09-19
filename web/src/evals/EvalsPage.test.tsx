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
