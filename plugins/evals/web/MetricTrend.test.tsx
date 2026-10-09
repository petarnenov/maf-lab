import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { EvalReportSummary, MetricComparison } from './types';
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

  it('groups the series by suite and variant with short option labels', () => {
    const retrieval = run('r1', '2026-09-20T10:00:00Z', 0.6);
    retrieval.variants[0].metrics = { mrr: 0.5, 'recall@5:bg': 0.55, 'recall@5': 0.6 };
    retrieval.variants.push({ ...retrieval.variants[0], name: 'dense', metrics: { mrr: 0.4 } });
    const selection: EvalReportSummary = {
      ...run('s1', '2026-09-20T11:00:00Z', 0),
      suite: 'selection',
      variants: [{ ...retrieval.variants[0], name: 'agent', metrics: { recall: 0.9 } }],
    };

    render(<MetricTrend reports={[retrieval, selection]} />);

    const picker = screen.getByRole('combobox', { name: 'Metric' });
    const groups = [...picker.querySelectorAll('optgroup')];
    expect(groups.map((g) => g.label)).toEqual([
      'selection · agent',
      'retrieval · hybrid',
      'retrieval · dense',
    ]);
    expect(
      within(groups[1])
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['recall@5', 'recall@5 · bg', 'mrr']);
    expect(within(groups[1]).getByRole('option', { name: 'recall@5 · bg' })).toHaveValue(
      'retrieval/hybrid/recall@5:bg',
    );
  });

  it('keeps the chosen series, and shows its group, when the reports refresh', async () => {
    const first = run('r1', '2026-09-20T10:00:00Z', 0.6);
    first.variants[0].metrics.mrr = 0.5;
    const second = run('r2', '2026-09-20T11:00:00Z', 0.7);
    second.variants[0].metrics.mrr = 0.55;

    const { rerender } = render(<MetricTrend reports={[first, second]} />);
    const picker = screen.getByRole('combobox', { name: 'Metric' });
    await userEvent.selectOptions(picker, 'retrieval/hybrid/mrr');

    const third = run('r3', '2026-09-20T12:00:00Z', 0.72);
    third.variants[0].metrics = { ...third.variants[0].metrics, mrr: 0.6, 'recall@5:en': 0.8 };
    third.variants.push({ ...third.variants[0], name: 'dense', metrics: { mrr: 0.4 } });
    rerender(<MetricTrend reports={[first, second, third]} />);

    expect(screen.getByRole('combobox', { name: 'Metric' })).toHaveValue('retrieval/hybrid/mrr');
    expect(screen.getByRole('combobox', { name: 'Metric' })).toHaveAccessibleDescription(
      'retrieval · hybrid',
    );
    expect(
      await screen.findByRole('img', { name: /retrieval\/hybrid\/mrr over time/ }),
    ).toBeInTheDocument();
  });

  it('renders nothing when there are no reports', () => {
    const { container } = render(<MetricTrend reports={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
