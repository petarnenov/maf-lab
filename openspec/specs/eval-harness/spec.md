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

`evals/` also holds datasets the harness does not read, because what executes them is not the harness: the A2A
conformance scenarios, the hostile verdicts, and the recorded runs. They live with the others because they are
the same kind of thing — a list of cases kept outside the code that checks them.

A retrieval case MAY declare the language its query is written in. A case without one SHALL be treated as the
corpus language, so existing datasets keep working unchanged.

A retrieval case MAY instead be an off-domain case: a question this corpus genuinely cannot answer, for which
the right retrieval is none at all. Such a case SHALL declare no relevant chunks and SHALL be marked as
off-domain, so that a row with no relevant chunks is never mistaken for a row somebody forgot to label. The
dataset SHALL hold off-domain cases, because a suite made only of questions the corpus can answer can show that
a relevance floor does no harm but never that it does its job.

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

#### Scenario: An off-domain case
- **WHEN** the harness loads a retrieval row marked off-domain
- **THEN** the case carries its query and the expectation that nothing is retrieved for it

#### Scenario: A dataset the harness does not read
- **WHEN** the harness loads its datasets
- **THEN** it does not require the conformance, verdict or recorded-run files to be present

### Requirement: Metrics
The harness SHALL compute selection recall and precision; retrieval recall@5,
recall@20 and MRR; generation faithfulness and answer relevance by an
LLM judge with a fixed rubric; and injection pass rate.

Retrieval metrics SHALL also be reported per query language, so that a suite passing overall cannot hide a
language that retrieves nothing useful.

The retrieval suite SHALL also report how often an off-domain case correctly retrieved nothing. That metric
SHALL be computed over the off-domain cases alone and SHALL be reported separately from recall, which is computed
over the answerable cases alone; averaging the two together would let a gain in one hide a loss in the other.
Both SHALL be able to gate the configured production variant.

#### Scenario: Selection expectations
- **WHEN** the selection eval runs
- **THEN** "what is the procedure when a fee schedule is missing" expects `search_documents`, "status of run 4417" expects `get_billing_run_status`, "why did run 4417 fail" expects both, and "thanks, that's all" expects none

#### Scenario: Recall per language
- **WHEN** the retrieval eval runs over a dataset containing cases in more than one language
- **THEN** the report gives recall@5 for each language as well as overall


#### Scenario: Silence on the unanswerable
- **WHEN** the retrieval eval runs over a dataset containing off-domain cases
- **THEN** the report gives the share of them that retrieved nothing, separately from recall over the answerable cases

#### Scenario: Recall is not diluted
- **WHEN** off-domain cases are added to the dataset
- **THEN** recall@5, recall@20 and MRR are still computed over the answerable cases only

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

### Requirement: The scenarios an outside client must pass are a dataset
What a client that shares no code with this system must be able to do over A2A SHALL be listed in
`evals/a2a-conformance.jsonl`, one scenario per row, and SHALL be executed by such a client rather than by the
harness — which links against this system and so could not prove the claim. The run SHALL report the share of
scenarios that passed, in the same shape the other suites report, and SHALL name every one that did not.

#### Scenario: The list is what runs
- **WHEN** a scenario is added to the dataset
- **THEN** the conformance run executes it without any other change

#### Scenario: A failure is named
- **WHEN** a scenario fails
- **THEN** the report names it and the run does not pass

#### Scenario: Run by an outsider
- **WHEN** the conformance scenarios run
- **THEN** they are driven by a client built from the agent card alone

### Requirement: A hostile verdict is a fixture, not a hope
Verdicts a broken or hostile reviewer might send SHALL be held in `evals/injection-a2a.jsonl`, and each one SHALL
be driven through the check that decides whether a verdict is believed and through the write flow. None of them
SHALL change what executes, and a verdict naming another account or adjustment SHALL be treated as a failed
review rather than as an answer.

#### Scenario: An embedded instruction
- **WHEN** a verdict's text tells the system to approve something else
- **THEN** nothing is proposed or applied for that something else

#### Scenario: A verdict about another account
- **WHEN** a verdict names an account other than the one asked about
- **THEN** it is not treated as a verdict

### Requirement: The browser is proved against runs the server produced
Runs captured from the running stack SHALL be held in `evals/ui-events.jsonl`, each with the state the browser
should end in, and replaying one SHALL produce that state. A change to what the server emits SHALL therefore be
visible as a failure in the browser's own tests.

A recorded run SHALL carry the moment it was captured, and SHALL be replayed as of that moment. A recording
carries real timestamps — the expiry of a proposal above all — so replaying it against the clock of the day it is
run would make a recording decay into a failure that says nothing about the code.

#### Scenario: A recorded run
- **WHEN** a recorded run is replayed through the reducer
- **THEN** the answer, the tool calls, the sources and the pending write match what the recording says

#### Scenario: A run that paused
- **WHEN** the recorded run is one that paused for a confirmation
- **THEN** replaying it leaves the turn waiting on that proposal

#### Scenario: A recording that has been kept a while
- **WHEN** a run recorded long enough ago that its proposal's expiry has passed is replayed
- **THEN** it still produces the state it recorded, because it is replayed as of when it was captured
