# Proposal

## Why

The Trend card on `/evals` offers every eval series in one flat dropdown of ~50 `suite/variant/metric` strings, ordered
by how many runs each has and then alphabetically. New metrics land wherever their run count puts them, so related
series end up scattered (retrieval's `bg-latn` series sit after `confirmation`), and finding "hybrid recall@5 in
Bulgarian" means reading the whole list.

## What Changes

- The metric picker groups its options by suite and variant (one labelled group per `suite · variant`), in a stable
  order: known suites in pipeline order (selection, retrieval, generation, injection, confirmation, intent,
  a2a-conformance), any other suite after them alphabetically; within retrieval the production variant (hybrid) first.
- Within a group, a metric's breakdowns (`recall@5:bg`) follow the metric they break down, languages (en, bg, bg-latn)
  before other splits (design, holdout); a metric that is broken down leads its group, followed by the rest of its
  family (`recall@20`), then the remaining metrics alphabetically.
- Options inside a group drop the redundant prefix (`recall@5 · bg` under `retrieval · hybrid`); the chosen series' group
  stays visible next to the closed picker, and a series' identity (its full id) is unchanged.
- The grouping is derived from the series ids the reports contain, so a new suite, variant or metric lands in the right
  group without a code change.
- The picker never forces horizontal scrolling at phone width.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `web-ui`: the "Evals screen" requirement gains how the metric picker groups and orders its series.

## Impact

- `web/src/evals/MetricTrend.tsx`, a new pure grouping module `web/src/evals/seriesGroups.ts` with tests,
  `MetricTrend.module.css`.
- No API, dataset or report-format change; no new dependency.
