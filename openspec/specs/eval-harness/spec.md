# eval-harness Specification

## Purpose
Measures tool selection, retrieval quality, answer quality, and injection
resistance on demand, so that changes to prompts, tools, models, or chunking
are judged by numbers rather than impressions.

## Requirements

### Requirement: JSONL datasets
The harness SHALL read datasets from `evals/`: `selection.jsonl` (question,
expectedTools — empty means no tool), `retrieval.jsonl` (query,
relevantChunkIds), `generation.jsonl` (question, reference answer, expected
source docIds), `injection.jsonl` (question, forbidden strings, forbidden
tenant ids), and `confirmation.jsonl` (a proposal and the facts its summary
must state).

A retrieval case MAY declare the language its query is written in. A case without one SHALL be treated as the
corpus language, so existing datasets keep working unchanged.

#### Scenario: Selection coverage
- **WHEN** the selection dataset is loaded
- **THEN** it contains obvious-docs, obvious-data, boundary (two tools), and negative cases

#### Scenario: Dataset row
- **WHEN** the harness loads `retrieval.jsonl`
- **THEN** each row contributes its query, the chunk ids that should be retrieved, and the language of the query when it declares one

#### Scenario: Case without a language
- **WHEN** a row declares no language
- **THEN** it is counted as the corpus language

#### Scenario: A confirmation case
- **WHEN** the harness loads `confirmation.jsonl`
- **THEN** each row contributes the proposal to make and the facts the summary put to a person must state

### Requirement: Metrics
The harness SHALL compute selection recall and precision; retrieval recall@5,
recall@20 and MRR; generation faithfulness and answer relevance by an
LLM judge with a fixed rubric; and injection pass rate.

Retrieval metrics SHALL also be reported per query language, so that a suite passing overall cannot hide a
language that retrieves nothing useful.

#### Scenario: Selection expectations
- **WHEN** the selection eval runs
- **THEN** "what is the procedure when a fee schedule is missing" expects `search_documents`, "status of run 4417" expects `get_billing_run_status`, "why did run 4417 fail" expects both, and "thanks, that's all" expects none

#### Scenario: Recall per language
- **WHEN** the retrieval eval runs over a dataset containing cases in more than one language
- **THEN** the report gives recall@5 for each language as well as overall

### Requirement: Retrieval mode comparison
The retrieval eval SHALL run in hybrid, dense-only, and sparse-only modes, and
with rerank and contextual retrieval toggled, reporting each mode side by side.

#### Scenario: Three modes reported
- **WHEN** the retrieval eval runs with default options
- **THEN** the report contains metrics for hybrid, dense-only, and sparse-only

### Requirement: Reports and thresholds
Each run SHALL write a JSON report and a Markdown summary readable by the web
app. Pass/fail thresholds SHALL come from configuration. The harness SHALL run
only on demand and be documented as required after changes to prompts, tool
descriptions, the model, the tool set, or chunking configuration.

The repository SHALL also hold a **baseline** of the metrics currently accepted, per suite and variant, so that what
"good" is today is stated where it can be reviewed rather than remembered. Every run SHALL compare its metrics with
that baseline and SHALL fail when a metric has dropped by more than a configured tolerance, naming the suite, the
variant, the metric, the baseline value, the new value and the size of the drop. A metric at or above its baseline,
or within the tolerance, SHALL NOT fail the run.

A metric the baseline does not mention SHALL be reported as new rather than ignored, so that adding a metric cannot
quietly escape the comparison. A baseline that mentions a metric the run did not produce SHALL be reported as
missing.

The baseline SHALL only change when it is explicitly asked to, never as a side effect of running the suites, so that
a regression cannot be absorbed by running them again. The report SHALL carry the comparison, so the screen can show
what moved without recomputing it.

#### Scenario: Threshold failure
- **WHEN** a metric is below its configured threshold
- **THEN** the report marks that suite as failed and the command exits non-zero

#### Scenario: A drop beyond the tolerance fails the run
- **WHEN** a metric is below its baseline by more than the tolerance
- **THEN** the run fails, naming the suite, variant, metric, both values and the drop, and the command exits non-zero

#### Scenario: Noise within the tolerance passes
- **WHEN** a metric is below its baseline by no more than the tolerance
- **THEN** the run passes and the comparison still reports the difference

#### Scenario: An improvement never fails
- **WHEN** a metric is above its baseline
- **THEN** the run passes and the comparison reports the gain

#### Scenario: A metric with no baseline
- **WHEN** a run produces a metric the baseline does not mention
- **THEN** the comparison reports it as new and the run does not fail on it

#### Scenario: The baseline only moves when asked
- **WHEN** a run's metrics are better than the baseline and no one asked to accept them
- **THEN** the baseline file is unchanged

#### Scenario: Accepting the baseline
- **WHEN** the run is asked to accept its results
- **THEN** the baseline records those metrics, with the run they came from and when

### Requirement: Feedback import
The harness SHALL import labeled feedback rows so that the next run includes
them.

#### Scenario: Wrong-document feedback
- **WHEN** a user flagged "wrong document" and a reviewer labeled it
- **THEN** the next retrieval eval run includes that row

### Requirement: The confirmation summary is measured
A suite SHALL check that the summary a person is asked to approve states the facts of the proposal it belongs
to — the account, the amount and the resulting fee — and reports the share of cases that do as its metric. A
summary that states something the proposal does not say SHALL fail its case.

#### Scenario: A faithful summary
- **WHEN** a proposal's summary states its account, amount and resulting fee
- **THEN** the case passes

#### Scenario: A summary that says something else
- **WHEN** a summary names an amount the proposal does not make
- **THEN** the case fails and the report names it
