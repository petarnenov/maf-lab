# Tasks

## 1. Grouping

- [x] 1.1 Add `web/src/evals/seriesGroups.ts`: parse a series id into suite, variant, metric base and breakdown, and
      return ordered groups (`{ key, label, options: [{ value, label }] }`) by the suite, variant, metric and breakdown
      rules of the design. Verify `seriesGroups.test.ts` covers the retrieval and intent orders from the spec, an unknown
      suite and variant, a new language breakdown landing inside its group, and that option values stay the full id.

## 2. Picker

- [x] 2.1 Render the `MetricTrend` picker with one `<optgroup>` per group and short option labels, show the chosen
      series' group next to it (`aria-describedby`), and keep the default and the chosen series by id. Verify
      `MetricTrend.test.tsx`: optgroups present in order, option labels short, selecting a value by full id still works,
      and a re-render with more series keeps the chosen one.
- [x] 2.2 Constrain the picker to the card width in `MetricTrend.module.css`, and wrap the runs table in a horizontal
      scroll container. Verify at a 375 px viewport, with the live reports, that nothing inside `main` extends past the
      viewport except within that container.

## 3. Checks

- [x] 3.1 Run `make lint`, the web tests and `make specs`; all pass.
