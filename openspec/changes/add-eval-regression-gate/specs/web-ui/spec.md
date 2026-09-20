# Spec Delta

## MODIFIED Requirements

### Requirement: Evals screen
The `/evals` screen SHALL list recent runs of the selection, retrieval, and
generation (and injection) evals with their metrics over time.

For a chosen suite and metric, the screen SHALL show how it moved across past runs, with the accepted baseline
marked, so a slow decline is visible rather than inferred from a table. Where a run recorded a comparison with the
baseline, the screen SHALL show what regressed, what improved and what was new, in words and figures — never by
colour alone.

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
