import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { EvalReportSummary, MetricComparison } from '../api/types';
import { MetricTrend } from './MetricTrend';

const run = (
  runId: string,
  startedAt: string,
  recall: number,
  comparisons?: MetricComparison[],
): EvalReportSummary => ({
  runId,
  suite: 'retrieval',
  startedAt,
  passed: true,
  variants: [
    {
      name: 'hybrid',
      metrics: { 'recall@5': recall },
      thresholds: { 'recall@5': 0.6 },
      passed: true,
      cases: 49,
      failures: [],
    },
  ],
  comparisons,
});

describe('MetricTrend', () => {
  it('plots a metric across runs with the baseline marked', async () => {
    render(
      <MetricTrend
        reports={[
          run('r3', '2026-09-20T12:00:00Z', 0.687, [
            {
              variant: 'hybrid',
              metric: 'recall@5',
              baseline: 0.687,
              value: 0.687,
              delta: 0,
              status: 'Improvement',
            },
          ]),
          run('r1', '2026-09-20T10:00:00Z', 0.456),
          run('r2', '2026-09-20T11:00:00Z', 0.61),
        ]}
      />,
    );

    const chart = await screen.findByRole('img', { name: /recall@5 over time/ });
    // Oldest first, one point per run.
    expect(chart.querySelectorAll('circle')).toHaveLength(3);
    expect(chart.querySelector('polyline')).not.toBeNull();
    // The baseline is drawn and labelled, not only implied.
    expect(within(chart).getByText(/baseline 0\.687/)).toBeInTheDocument();
    expect(screen.getByText(/3 runs/)).toHaveTextContent('0.456');
    expect(screen.getByText(/3 runs/)).toHaveTextContent('0.687');
  });

  it('says there is not enough history instead of drawing an empty chart', () => {
    render(<MetricTrend reports={[run('r1', '2026-09-20T10:00:00Z', 0.687)]} />);

    expect(screen.getByText(/Not enough history yet/)).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('lets another metric be chosen', async () => {
    const two = run('r1', '2026-09-20T10:00:00Z', 0.6);
    two.variants[0].metrics.mrr = 0.5;
    const later = run('r2', '2026-09-20T11:00:00Z', 0.7);
    later.variants[0].metrics.mrr = 0.55;

    render(<MetricTrend reports={[two, later]} />);
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Metric' }),
      'retrieval/hybrid/mrr',
    );

    expect(await screen.findByRole('img', { name: /mrr over time/ })).toBeInTheDocument();
  });

  it('renders nothing when there are no reports', () => {
    const { container } = render(<MetricTrend reports={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
