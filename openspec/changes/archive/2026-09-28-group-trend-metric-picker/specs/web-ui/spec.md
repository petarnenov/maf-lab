## MODIFIED Requirements

### Requirement: Evals screen
The `/evals` screen SHALL list recent runs of the selection, retrieval, and
generation (and injection) evals with their metrics over time.

For a chosen suite and metric, the screen SHALL show how it moved across past runs, with the accepted baseline
marked, so a slow decline is visible rather than inferred from a table. Where a run recorded a comparison with the
baseline, the screen SHALL show what regressed, what improved and what was new, in words and figures — never by
colour alone.

The metric picker SHALL group its series by suite and variant, one labelled group per suite and variant, and SHALL
derive the groups from the series the reports contain, so a suite, variant or metric that first appears in a new run
lands in its group without a change to the screen. Groups SHALL be ordered by suite — selection, retrieval,
generation, injection, confirmation, intent, a2a-conformance, then any other suite alphabetically — and, within a
suite, by variant, the production variant first (for retrieval: hybrid, hybrid-dbsf, dense, sparse), then any other
variant alphabetically. Within a group, a metric that has breakdowns (a `metric:part` series) SHALL come first, followed
by the other metrics of its family (the same name before `@`, by ascending number), then the remaining metrics
alphabetically; each metric's breakdowns SHALL follow it directly, languages first (en, bg, bg-latn, then any other
language code) and other splits after them alphabetically. An option inside a group SHALL name only the metric and
its breakdown, the chosen series' suite and variant SHALL stay visible while the picker is closed, and choosing a
series SHALL still identify it by its full suite, variant and metric. At phone width the picker SHALL fit the screen,
and the runs table SHALL scroll within itself, so neither makes the page scroll sideways.

#### Scenario: Reports listed
- **WHEN** eval reports exist
- **THEN** the screen shows each run's date, suite, mode and metrics in a table

#### Scenario: A metric over time
- **WHEN** several runs of a suite exist
- **THEN** the screen plots that suite's metric across those runs with the baseline marked

#### Scenario: A regression is named
- **WHEN** a run recorded a metric that dropped below its baseline beyond the tolerance
- **THEN** the screen names the metric, both values and the drop

#### Scenario: Too little history
- **WHEN** fewer than two runs of a suite exist
- **THEN** the screen says there is not enough history rather than drawing an empty chart

#### Scenario: Series grouped by suite and variant
- **WHEN** reports exist for retrieval (variants dense, hybrid) and selection (variant agent)
- **THEN** the picker offers the groups `selection · agent`, `retrieval · hybrid`, `retrieval · dense` in that order, each option inside naming only its metric

#### Scenario: Breakdowns follow their metric
- **WHEN** a retrieval variant reports `mrr`, `offDomainSilence`, `recall@20`, `recall@5`, `recall@5:bg`, `recall@5:bg-latn` and `recall@5:en`
- **THEN** its group lists `recall@5`, `recall@5 · en`, `recall@5 · bg`, `recall@5 · bg-latn`, `recall@20`, `mrr`, `offDomainSilence`

#### Scenario: Languages before other splits
- **WHEN** an intent variant reports `accuracy`, `accuracy:holdout`, `accuracy:bg`, `accuracy:design`, `accuracy:en` and `forcedWhenShould`
- **THEN** its group lists `accuracy`, `accuracy · en`, `accuracy · bg`, `accuracy · design`, `accuracy · holdout`, `forcedWhenShould`

#### Scenario: A suite the screen has never seen
- **WHEN** a report arrives for a suite outside the known order
- **THEN** its group is listed after every known suite's groups, and its metrics are grouped and ordered by the same rules

#### Scenario: A new metric joins its group
- **WHEN** a newer run adds a breakdown such as `recall@5:de` to a variant that already had `recall@5`
- **THEN** the new series is listed inside that variant's group right after `recall@5`'s other language breakdowns, not at the end of the picker

#### Scenario: The chosen series survives a refresh
- **WHEN** a series has been chosen and the reports are fetched again with more series
- **THEN** the same series stays chosen and its suite and variant stay visible next to the picker

#### Scenario: Phone width
- **WHEN** the screen is shown 375 px wide with reports whose metrics column is wider than that
- **THEN** the Trend card and its picker fit inside the viewport and only the runs table scrolls sideways
