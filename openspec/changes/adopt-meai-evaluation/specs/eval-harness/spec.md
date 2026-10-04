# Spec Delta

## MODIFIED Requirements

### Requirement: JSONL datasets
The harness SHALL read datasets from `evals/`: `selection.jsonl` (question,
expectedTools — empty means no tool), `retrieval.jsonl` (query,
relevantChunkIds), `generation.jsonl` (question, reference answer, the reference answer's atomic statements as
reference points, expected source docIds), `generation-judge.jsonl` (question, answer, reference points, and for each
point whether a reviewer found it stated and whether contradicted), `injection.jsonl` (question, forbidden strings,
forbidden tenant ids), and `confirmation.jsonl` (a proposal and the facts its summary must state).

Every generation case SHALL carry at least one reference point. A point SHALL state one fact, step or condition, so
that whether an answer states it is a yes/no judgment.

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

#### Scenario: A generation case without reference points
- **WHEN** the harness loads a generation row with no reference points
- **THEN** loading fails and names the row

### Requirement: Metrics
The harness SHALL compute selection recall and precision; retrieval recall@5,
recall@20 and MRR; and injection pass rate.

The generation suite SHALL grade each answer with Jev, using the shared client and the pinned model, in one request
per case. Each question SHALL be a closed yes/no judgment about one sentence, one reference point or one source.
Code SHALL split the answer into sentences, count the answers and compute the metrics; Jev SHALL NOT be asked to
count, compute or compare numbers or dates. A Noul SHALL count as yes at 0.5 or above. Per case:
- `faithfulness` — the share of the answer's claim sentences that its sources support, 1 when no sentence makes a claim;
- `relevance` — 1 when the answer addresses the question, else 0;
- `completeness` — the share of the case's reference points the answer states;
- `referenceAgreement` — 1 minus the share of reference points the answer contradicts;
- `retrievalJudged` — the share of the sources the turn read that address the question's subject, 1 when it read none;
- `judgeUncertain` — the share of the case's answers that fell between 0.2 and 0.8.

The run SHALL report the mean of each. A case SHALL pass when `faithfulness` reaches the suite's pass mark and
`relevance` is 1. A failed case's reason SHALL name the unsupported sentences and the reference points missed or
contradicted. `completeness`, `referenceAgreement` and `retrievalJudged` SHALL be gated by thresholds and the baseline
like any metric, but SHALL NOT fail a case on their own. `judgeUncertain` SHALL have no threshold.

The suite SHALL refuse to run without the Jev key rather than report a judge that graded nothing. A case whose
request fails during the run SHALL score 0 on every judged metric and SHALL name the reason, so that a broken judge
cannot pass the suite.

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

#### Scenario: One request per case
- **WHEN** the generation suite grades a case whose answer has nine sentences, four reference points and five sources
- **THEN** exactly one Jev request is sent for that case, and the counts behind each metric are computed in code

#### Scenario: Clearly supported answer (high confidence)
- **WHEN** every claim sentence of an answer is supported with probability 0.8 or more, the answer addresses the question, and it states every reference point
- **THEN** the case scores 1 on `faithfulness`, `relevance` and `completeness`, passes, and adds nothing to `judgeUncertain`

#### Scenario: One invented step (low confidence)
- **WHEN** one of four claim sentences adds a step that no source holds and Jev answers 0.1 for it
- **THEN** the case's `faithfulness` is 0.75 and its failure reason, if it fails, quotes that sentence

#### Scenario: A sentence in the review band (medium confidence)
- **WHEN** Jev answers 0.45 for whether a claim sentence is supported
- **THEN** the sentence counts as unsupported, and the case's `judgeUncertain` includes that answer

#### Scenario: An answer that makes no claim
- **WHEN** the answer only says that the assistant cannot answer from what it found
- **THEN** its `faithfulness` is 1, and `completeness` shows what it left out

#### Scenario: A contradicted reference point
- **WHEN** the answer states a deadline that conflicts with a reference point
- **THEN** `referenceAgreement` drops for that case and the failure reason names the point

#### Scenario: Jev fails during the run (fallback)
- **WHEN** a case's Jev request times out
- **THEN** that case scores 0 on every judged metric with the reason, and the run continues with the next case

#### Scenario: No Jev key
- **WHEN** the generation suite is started without the Jev key
- **THEN** it refuses to run and names the missing variable

#### Scenario: A Bulgarian answer
- **WHEN** the agent answers a Bulgarian question in Bulgarian
- **THEN** it is split and graded like an English one, and its scores are reported like any other case

### Requirement: The generation suite reports Jev's answer check
The `generation` suite SHALL read, for each case, the verdict and probabilities of Jev's answer check from the turn it
ran — without a request for it — and SHALL report them beside the grade: in each case's progress line and failure
reason, and as metrics of the run:
- the share of cases Jev checked;
- the share of checked cases Jev found `uncertain`;
- over the checked cases, how often the check's grounding verdict agrees with the grade's faithfulness passing, and
  how often its relevance verdict agrees with the grade's relevance — where the check agrees with a pass when it raised
  no signal for that question (`pass` or `uncertain`).

The agreement metrics SHALL be omitted when no case was checked, rather than reported as zero. They SHALL have no
pass/fail threshold of their own. The generation dataset SHALL hold codebase questions, in English and in Bulgarian,
whose expected sources are repository paths, beside the billing ones.

#### Scenario: Agreement reported
- **WHEN** the generation suite runs and Jev checks every case's answer
- **THEN** the report carries the checked share, the uncertain share and both agreement metrics beside faithfulness and relevance

#### Scenario: Jev unavailable during the eval
- **WHEN** no case's answer could be checked during its turn
- **THEN** the report carries a checked share of 0 and no agreement metric, and the grade's metrics are unaffected

#### Scenario: A Bulgarian codebase question
- **WHEN** the generation suite runs a Bulgarian question about the repository
- **THEN** the turn searches the codebase, its source recall is measured against the expected paths, and its answer check is reported like any other case

## ADDED Requirements

### Requirement: The generation judge is measured on labelled answers
The harness SHALL have a `generation-judge` suite that measures the generation grade on its own, without the
answering model and without tools:
- over `evals/answer-check.jsonl`, whether the grade's faithfulness fails exactly on the answers a reviewer found
  unsupported, and its relevance on the answers found off the question;
- over `evals/generation-judge.jsonl`, whether each reference point is graded stated or contradicted as the reviewer
  labelled it.

It SHALL report accuracy overall and per domain, language and split. Beside its own, it SHALL report the production
answer check's accuracy on the same `answer-check.jsonl` rows. It SHALL name every case it got wrong with the answers
behind it, SHALL show a progress line per case over the known number of cases, SHALL refuse to run without the Jev
key, and SHALL gate against thresholds and the baseline like the other suites. `generation-judge.jsonl` SHALL hold
English and Bulgarian cases and SHALL carry no firm's client data.

#### Scenario: An unsupported answer caught (low confidence)
- **WHEN** a labelled-unsupported answer is graded and one of its sentences is answered 0.1 for support
- **THEN** its faithfulness is below the pass mark and the case counts as correct

#### Scenario: A supported Bulgarian answer (high confidence)
- **WHEN** a Bulgarian answer labelled supported is graded and every claim sentence is answered 0.8 or more
- **THEN** the case counts as correct and is reported under Bulgarian

#### Scenario: A point in the review band (medium confidence)
- **WHEN** a reference point labelled stated is answered 0.4
- **THEN** it counts as not stated, the case counts as wrong, and the report names the case with 0.4

#### Scenario: Jev fails on a case (fallback)
- **WHEN** a case's request fails
- **THEN** the case counts as wrong with the reason, and the run continues

#### Scenario: The same input twice
- **WHEN** the suite runs twice over unchanged datasets with the same pinned model
- **THEN** every case lands on the same side of each judgment in both runs, unless one of its answers sits in the review band, where a probability near the 0.5 cut may move between runs

#### Scenario: Beside the production check
- **WHEN** the suite finishes
- **THEN** its report shows the grade's accuracy and the production answer check's accuracy on the same rows, per language

### Requirement: Generation runs leave a browsable report
Each `generation` and `generation-judge` run SHALL write a browsable report beside its JSON and Markdown reports. The
report SHALL show every case's scores per metric with the sentences, points and sources behind them, and how the
scores moved across the runs kept. Kept results and the report contain dataset questions, answers and sources; they
SHALL be stored with the eval reports, outside version control, and SHALL never be written to logs.

#### Scenario: Browsable report
- **WHEN** a generation run finishes
- **THEN** a browsable report for that run sits beside its JSON and Markdown reports and lists every case with its scores and the unsupported sentences

#### Scenario: Nothing reaches the logs
- **WHEN** a generation run grades a case
- **THEN** the logs carry the request's model, token usage, latency and counts, and never a sentence, a point or a source's text

### Requirement: A suite that drives the live agent is read as the mean of several runs
Configuration SHALL be able to set, per suite, how many times one invocation runs it, and the command line SHALL be
able to override that for one invocation. When a suite runs more than once:
- each run SHALL write its own report;
- the metrics the gate compares, the thresholds check and the baseline accepts SHALL be each metric's mean over the
  runs, a metric some runs lack being averaged over the runs that report it;
- the combined report SHALL name its runs and keep every run's failures, each marked with its run.

The `generation` suite SHALL run three times by default, because the agent answers differently on every run and a
single run in the tail of that spread reads as a regression, or, accepted as the baseline, makes later runs read as
regressions.

#### Scenario: One bad run among three
- **WHEN** the generation suite runs three times and one run's faithfulness is well below the other two
- **THEN** the gate compares the mean of the three runs, and that run's failing cases are listed marked with its run

#### Scenario: A mean below the threshold
- **WHEN** one of three runs clears a threshold and the mean of the three does not
- **THEN** the suite fails

#### Scenario: Accepting from several runs
- **WHEN** the baseline is accepted from an invocation that ran the suite ten times
- **THEN** the baseline holds each metric's mean over the ten runs, and each run's report is kept

#### Scenario: A single run
- **WHEN** a suite is configured to run once
- **THEN** its report and gate behave exactly as before

