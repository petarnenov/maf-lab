import { describe, expect, it } from 'vitest';
import { groupSeries } from './seriesGroups';

const labels = (values: string[]) =>
  groupSeries(values).map((g) => [g.label, g.options.map((o) => o.label)]);

describe('groupSeries', () => {
  it('groups by suite then variant, known suites in pipeline order and production retrieval first', () => {
    const groups = groupSeries([
      'retrieval/dense/mrr',
      'intent/jev/accuracy',
      'a2a-conformance/partner/passRate',
      'retrieval/sparse/mrr',
      'selection/agent/recall',
      'retrieval/hybrid/mrr',
      'generation/agent/faithfulness',
      'confirmation/agent/faithfulness',
      'retrieval/hybrid-dbsf/mrr',
      'injection/agent/passRate',
    ]);

    expect(groups.map((g) => g.label)).toEqual([
      'selection · agent',
      'retrieval · hybrid',
      'retrieval · hybrid-dbsf',
      'retrieval · dense',
      'retrieval · sparse',
      'generation · agent',
      'injection · agent',
      'confirmation · agent',
      'intent · jev',
      'a2a-conformance · partner',
    ]);
  });

  it('puts a broken-down metric first, its breakdowns and family next, languages in script order', () => {
    expect(
      labels([
        'retrieval/hybrid/mrr',
        'retrieval/hybrid/recall@20',
        'retrieval/hybrid/recall@5',
        'retrieval/hybrid/recall@5:bg-latn',
        'retrieval/hybrid/offDomainSilence',
        'retrieval/hybrid/recall@5:en',
        'retrieval/hybrid/recall@5:bg',
      ]),
    ).toEqual([
      [
        'retrieval · hybrid',
        [
          'recall@5',
          'recall@5 · en',
          'recall@5 · bg',
          'recall@5 · bg-latn',
          'recall@20',
          'mrr',
          'offDomainSilence',
        ],
      ],
    ]);
  });

  it('lists per-language breakdowns before per-split ones', () => {
    expect(
      labels([
        'intent/jev/unforcedWhenShouldNot',
        'intent/jev/accuracy:holdout',
        'intent/jev/accuracy:bg',
        'intent/jev/forcedWhenShould',
        'intent/jev/accuracy:design',
        'intent/jev/accuracy',
        'intent/jev/accuracy:en',
        'intent/jev/accuracy:bg-latn',
      ]),
    ).toEqual([
      [
        'intent · jev',
        [
          'accuracy',
          'accuracy · en',
          'accuracy · bg',
          'accuracy · bg-latn',
          'accuracy · design',
          'accuracy · holdout',
          'forcedWhenShould',
          'unforcedWhenShouldNot',
        ],
      ],
    ]);
  });

  it('keeps metrics without breakdowns alphabetical', () => {
    expect(
      labels([
        'selection/agent/recall',
        'selection/agent/precision',
        'selection/agent/exactMatch',
        'selection/agent/negativeAccuracy',
      ]),
    ).toEqual([['selection · agent', ['exactMatch', 'negativeAccuracy', 'precision', 'recall']]]);
  });

  it('gives an unknown suite and variant their own groups after the known ones', () => {
    expect(
      groupSeries([
        'zeta/b/score',
        'latency/p95/ms',
        'retrieval/rerank/mrr',
        'retrieval/hybrid/mrr',
        'selection/agent/recall',
      ]).map((g) => g.label),
    ).toEqual([
      'selection · agent',
      'retrieval · hybrid',
      'retrieval · rerank',
      'latency · p95',
      'zeta · b',
    ]);
  });

  it('files a newly appearing breakdown inside its group, not at the end', () => {
    const groups = groupSeries([
      'retrieval/hybrid/recall@5',
      'retrieval/hybrid/recall@5:en',
      'retrieval/hybrid/recall@5:bg',
      'retrieval/hybrid/recall@5:bg-latn',
      'retrieval/hybrid/mrr',
      'selection/agent/recall',
      // First seen in the newest run.
      'retrieval/hybrid/recall@5:de',
    ]);

    expect(groups[1].options.map((o) => o.label)).toEqual([
      'recall@5',
      'recall@5 · en',
      'recall@5 · bg',
      'recall@5 · bg-latn',
      'recall@5 · de',
      'mrr',
    ]);
  });

  it('keeps the full series id as the value', () => {
    const [group] = groupSeries(['retrieval/hybrid/recall@5:bg']);
    expect(group.options).toEqual([
      { value: 'retrieval/hybrid/recall@5:bg', label: 'recall@5 · bg' },
    ]);
  });
});
