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
It SHALL also report hybrid search with the relevance gate in the opposite state to production, so a run shows
what the gate changes; and when rerank is toggled it SHALL report each available reranker as its own variant. Judge
failures during a run (timeouts, rejected requests) SHALL be counted and printed apart from the quality metrics, so an
outage of the judge is not read as a quality result.

#### Scenario: Three modes reported
- **WHEN** the retrieval eval runs with default options
- **THEN** the report contains metrics for hybrid, dense-only, and sparse-only

#### Scenario: The gate compared
- **WHEN** the retrieval eval runs with default options
- **THEN** the report also contains hybrid with the relevance gate flipped relative to production, including off-domain silence and recall per language

#### Scenario: Rerankers compared
- **WHEN** the retrieval eval runs with rerank toggled on
- **THEN** the report contains one hybrid variant per reranker, side by side

#### Scenario: Judge failures reported apart
- **WHEN** relevance requests time out or are rejected during a run
- **THEN** the run prints how many failed and why, per variant, separately from recall, MRR and silence

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

The tolerance a metric is compared against SHALL be that metric's. Configuration SHALL be able to set one for a
named metric of a named suite, and SHALL fall back to the suite's and then to the default for any metric that has
none. A tolerance SHALL be derived from that metric's measured run-to-run spread and SHALL be recorded with what
it was measured from; a tolerance chosen to make a particular run pass has no basis to be reviewed against later.

A metric that does not move between identical runs SHALL NOT inherit a tolerance widened for a different metric.
Noise is a property of how a metric is produced — a live translation, a model judging prose — and not of the suite
that reports it, so a suite-wide number is either too loose for its stable metrics or too tight for its noisy ones.

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

#### Scenario: A metric with its own tolerance
- **WHEN** a metric has a tolerance configured for it and drops by less than that but more than its suite's
- **THEN** the run passes

#### Scenario: A stable metric beside a noisy one
- **WHEN** a metric with no tolerance of its own drops by more than the default, in a suite whose tolerance was widened for another metric
- **THEN** the run fails

#### Scenario: Nothing configured for a metric
- **WHEN** a metric has no tolerance of its own
- **THEN** its suite's tolerance applies, and the default where the suite has none

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

### Requirement: Intent classification is measured directly
The harness SHALL have an `intent` suite that measures the intent classifier on its own — without the answering model
and without tools — over `evals/intent.jsonl`, whose cases give a question, whether retrieval should be forced, a
category and a language. The dataset SHALL include questions outside the documented domain phrased as procedures, in
every language and script the dataset covers, because a dataset made only of in-domain questions cannot show a
classifier that forces retrieval for everything phrased as a procedure. The suite SHALL report forcing accuracy, the
share of should-not-force cases correctly left unforced, and the share of should-force cases forced — each reported
separately so a gain in one cannot hide a loss in the other — overall and per language, SHALL name every case it got
wrong, and SHALL gate against thresholds and the baseline like the other suites.

#### Scenario: Off-domain procedural question forced
- **WHEN** the classifier forces retrieval for "How do I cook carbonara?"
- **THEN** the `intent` report counts it against the share of should-not-force cases left unforced, and names the case

#### Scenario: Per language
- **WHEN** the intent eval runs over cases in English, Bulgarian and Bulgarian written in Latin letters
- **THEN** the report gives forcing accuracy for each of them as well as overall

#### Scenario: No answering model
- **WHEN** the intent eval runs
- **THEN** no chat model is called and no tool runs

### Requirement: Retrieval is measured in every script questions arrive in
The retrieval dataset SHALL contain, for every Bulgarian case, a twin of the same question written in Latin letters,
marked with its own language, so that recall is reported for English, Bulgarian and Latin-script Bulgarian
separately. A change of dense embedding SHALL be judged on all three, and on the lowest of the three as well as on
their average.

#### Scenario: Latin-script recall is reported
- **WHEN** the retrieval eval runs
- **THEN** the report gives recall@5 for `en`, `bg` and `bg-latn`

#### Scenario: A language cannot hide behind the average
- **WHEN** a candidate embedding raises average recall@5 but lowers one language's
- **THEN** the report shows that language's recall next to the average, and the regression gate compares each language against its baseline

### Requirement: The guardrail is measured directly
The harness SHALL have a `guardrail` suite that measures the screens on their own — without the answering model and
without tools — over `evals/guardrail.jsonl`, whose cases give a text, the side it arrives from (a user's prompt, a tool
result, another agent's words), whether it is malicious, a category, a language and a split (design or held out). A
tool-side case MAY name the tool whose result it is; the suite SHALL screen it as that tool's item — a
`search_codebase` case in the codebase context with its record-only questions — and a case that names none as a
tool result of no search. The dataset SHALL include benign texts that look alarming — billing questions that say
"ignore", "override", "delete", "credit" or "approve", procedures that tell staff what to do, a document that quotes an
attack in order to warn about it, and codebase snippets of the repository's own prompt templates, agent instructions
and string literals that address an AI — because a guard measured only on attacks cannot show the legitimate traffic
it would refuse. It SHALL also include attacks planted in repository-shaped files, among them one addressed only to
the AI, so that what the record-only rule lets through is a named, counted case. It SHALL cover English, Bulgarian and
Bulgarian written in Latin letters. The suite SHALL report the share of malicious cases flagged and the share of benign
cases let through — each separately, so a guard that flags everything cannot hide behind one that flags nothing —
overall and per side, tool, language, category and split, SHALL report how many cases went unscreened because Jev did
not answer, SHALL name every case it got wrong, and SHALL gate against thresholds and the baseline like the other
suites.

#### Scenario: A legitimate question refused
- **WHEN** the guard flags "How do I delete a draft invoice before it is sent?"
- **THEN** the `guardrail` report counts it against the share of benign cases let through, and names the case

#### Scenario: Per language and per split
- **WHEN** the guardrail eval runs
- **THEN** the report gives both rates for English, Bulgarian and Latin-script Bulgarian, and for the held-out split on its own

#### Scenario: A repository prompt file
- **WHEN** a benign `search_codebase` case holds a system-prompt template that addresses the assistant
- **THEN** it is screened in the codebase context, and the report counts it as let through unless a question that can withhold a codebase snippet reaches the threshold

#### Scenario: Per tool
- **WHEN** the guardrail eval runs
- **THEN** the report gives both rates for the `search_codebase` cases on their own

#### Scenario: Jev did not answer
- **WHEN** a case's screening times out during the eval
- **THEN** it counts as not flagged, and the report's unscreened count includes it

#### Scenario: No answering model
- **WHEN** the guardrail eval runs
- **THEN** no chat model is called and no tool runs

### Requirement: Retrieval measured per domain
A retrieval eval row SHALL name its domain (`billing` when it names none). Each domain's rows SHALL be scored against
that domain's collection, the portfolio rows as the `portfolio-hybrid` variant with its own thresholds. The dataset
check SHALL verify each row's chunk ids against its own domain's corpus.

#### Scenario: Portfolio rows are not scored against billing
- **WHEN** the retrieval suite runs
- **THEN** the billing variants score only billing rows and `portfolio-hybrid` scores only portfolio rows

### Requirement: Review-queue labels stay in their domain
The review queue SHALL resolve each search's sources to chunk ids in that search's own domain collection. A retrieval
label SHALL record the domain of the search that found its chunks. A label whose chunks came from both domains SHALL
be refused with a message saying so.

#### Scenario: A portfolio search's chunks
- **WHEN** a reviewer labels the chunks a `search_portfolio_documents` call returned
- **THEN** the appended retrieval row carries `"domain": "portfolio"`

### Requirement: The generation suite reports Jev's answer check
The `generation` suite SHALL read, for each case, the verdict and probabilities of Jev's answer check from the turn it
ran — without a Jev request of its own — and SHALL report them beside the rubric's scores: in each case's progress line
and failure reason, and as metrics of the run:
- the share of cases Jev checked;
- the share of checked cases Jev found `uncertain`;
- over the checked cases, how often Jev's grounding verdict agrees with the rubric's faithfulness passing, and how
  often Jev's relevance verdict agrees with the rubric's relevance passing — where Jev agrees with a pass when it
  raised no signal for that question (`pass` or `uncertain`).

The agreement metrics SHALL be omitted when no case was checked, rather than reported as zero. They SHALL have no
pass/fail threshold of their own. The generation dataset SHALL hold codebase questions, in English and in Bulgarian,
whose expected sources are repository paths, beside the billing ones.

#### Scenario: Agreement reported
- **WHEN** the generation suite runs and Jev checks every case's answer
- **THEN** the report carries the checked share, the uncertain share and both agreement metrics beside faithfulness and relevance

#### Scenario: Jev unavailable during the eval
- **WHEN** no case's answer could be checked
- **THEN** the report carries a checked share of 0 and no agreement metric, and the rubric's metrics are unaffected

#### Scenario: A Bulgarian codebase question
- **WHEN** the generation suite runs a Bulgarian question about the repository
- **THEN** the turn searches the codebase, its source recall is measured against the expected paths, and its answer check is reported like any other case

### Requirement: How answers present card data is measured
A `presentation` suite SHALL run portfolio questions from a JSONL dataset through the agent. Each row carries an id, a
question, a firm, and whether the turn is expected to show a card. A rebalance question also carries the expected
verdict: needed or not needed.

The suite SHALL report:
- **`noTable`:** the share of carded turns whose answer contains no markdown table (two or more pipe-delimited lines).
- **`noRowList`:** the share of carded turns whose answer does not list the card's rows one by one (three or more list
  lines, each naming a different row: an asset class, an account id, a quarter end). A row marked `rowsRequested`,
  whose question explicitly asks about every row, is left out of this metric.
- **`figuresGrounded`:** the share of currency amounts in the answers that appear in the content of that turn's cards.
- **`verdictCorrect`:** the share of rebalance questions whose answer states the expected verdict. It SHALL be judged
  by a yes/no rubric.
- **`languageMatch`:** the share of answers written in the question's language, read from the script most of the
  answer's letters are in.

Each case below target SHALL be listed with its reason. The suite SHALL have thresholds and a baseline like the other
suites, and SHALL be runnable alone (`SUITE=presentation`) and as part of `all`. The dataset SHALL include English and
Bulgarian questions.

#### Scenario: A restated table is counted
- **WHEN** an answer to a carded question contains a markdown table
- **THEN** that case lowers `noTable` and is listed with the reason "table in answer"

#### Scenario: Rows listed one by one are counted
- **WHEN** an answer to a holdings question has a bullet for US equity, one for international equity and one for cash
- **THEN** that case lowers `noRowList` and is listed with the reason "card rows listed one by one"

#### Scenario: An invented amount is counted
- **WHEN** an answer quotes "8 500 $" and no card in that turn contains 8,500
- **THEN** that amount lowers `figuresGrounded`, and the case is listed with the amount

### Requirement: The codebase is measured as a domain
The domain dataset SHALL hold codebase questions in English, Bulgarian and Latin-script Bulgarian. It SHALL also hold
general-programming questions labelled `none`. A domain case's label SHALL name the domains in scope:
- one domain;
- several, joined in a fixed order, where the legacy label `both` means billing and portfolio;
- or `none`.

The selection dataset SHALL hold codebase cases expecting `search_codebase`. The eval host SHALL load the codebase
server beside the others.

#### Scenario: Codebase accuracy is reported
- **WHEN** the domain suite runs
- **THEN** the report gives accuracy over the codebase cases, and the general-programming cases count toward none-accuracy

### Requirement: The answer check is measured on labelled answers
The harness SHALL have an `answer-check` suite that measures Jev's answer check on its own — without the answering
model and without tools — over `evals/answer-check.jsonl`. Each case gives a question, the previous question (may be
empty), an answer, the sources and previous sources as the model read them, whether a reviewer found the answer
unsupported and whether off the question, the domain (billing, portfolio or codebase), the language (English, Bulgarian
or Latin-script Bulgarian) and a split (design or held out). The suite SHALL run each case through the production
check with the production configuration and report, overall and per domain, language and split:
- how many unsupported answers were flagged `not_grounded`, and how many supported answers were not;
- the same for relevance;
- how many cases fell in the review band, and how many were unchecked, with the reason.

It SHALL name every case it got wrong with its probabilities, SHALL show a progress line per case over the known
number of cases, SHALL refuse to run without the Jev key rather than report a check that flagged nothing, and SHALL
gate against thresholds and the baseline like the other suites. The dataset SHALL hold codebase cases that replay
reviewed turns — answers a reviewer judged right, expected to pass, and answers a reviewer judged wrong, expected to be
flagged — in English, Bulgarian and Latin-script Bulgarian, and billing cases, so a change to the codebase side cannot
move the billing side unseen. No case SHALL carry a firm's client data.

#### Scenario: A right code answer replayed (high confidence)
- **WHEN** a case replays a Bulgarian answer a reviewer judged right, whose cited snippets are among its sources
- **THEN** the suite counts it as correct when the check raises no signal, and names it otherwise

#### Scenario: A wrong code answer replayed (low confidence)
- **WHEN** a case replays an answer that states a constant no source holds
- **THEN** the suite counts it as correct only when the check's verdict is `not_grounded`

#### Scenario: A case in the review band (medium confidence)
- **WHEN** a case's grounding probability falls between the signal floor and the pass threshold
- **THEN** the report counts it in the band, and it counts as not flagged

#### Scenario: Jev did not answer (fallback)
- **WHEN** a case's check times out during the eval
- **THEN** it counts as not flagged, and the report's unchecked count includes it with the reason

#### Scenario: No answering model
- **WHEN** the answer-check eval runs
- **THEN** no chat model is called and no tool runs

### Requirement: Graph depth dataset
The harness SHALL read `evals/graph-depth.jsonl`, one case per row. A case SHALL be either a trace or an impact:
- A trace names a code symbol and a direction, callers or callees.
- An impact names a repository-relative C# file.

Every case SHALL carry:
- the question a developer would ask about it;
- the items an answer needs: method symbols for a trace, test files for an impact;
- for each needed item, the number of calls at which it is first reached.

A needed item that is not reachable within the deepest variant SHALL still be listed and marked as beyond reach. A
missing caller is then reported, not silently dropped. A row without needed items SHALL be rejected when the dataset
loads.

#### Scenario: A trace case
- **WHEN** the harness loads a trace row
- **THEN** the case carries its symbol, its direction, its question, and each needed method with the number of calls at which it is reached

#### Scenario: An impact case
- **WHEN** the harness loads an impact row
- **THEN** the case carries its file, its question, and each needed test file with the number of calls at which it is reached

#### Scenario: A row that lists nothing needed
- **WHEN** a row declares no needed items
- **THEN** loading the dataset fails and names the row

### Requirement: Graph depth comparison
The `graph-depth` suite SHALL run every case once for each of three variants: `depth-2`, `depth-3` and `depth-4`. In
each variant the code graph tools are pinned to that depth. It SHALL measure two layers.

The structural layer calls the graph tools directly, with no model. It SHALL report per variant:
- the mean share of needed items reached;
- the share of cases with every needed item reached;
- the same recall split by the depth each case needs, so a gain for deep cases is not hidden by an average over shallow
  ones;
- the share of returned nodes that were needed;
- the mean number of returned nodes;
- the mean size of the tool result in tokens;
- the share of results that were truncated;
- tool latency at p50 and p95.

The end-to-end layer asks the agent each case's question. It SHALL report per variant:
- faithfulness and relevance from the fixed rubric judge, scored against the case's needed items;
- the mean share of needed items the answer names;
- the share of turns that called a graph tool at all.

A turn that called no graph tool SHALL still be scored, and SHALL be counted in the tool-call share, so a variant cannot
look better by being ignored. Judge failures SHALL be counted and reported apart from the scores.

The suite SHALL be able to run the structural layer alone, with no chat model and no Jev key.

#### Scenario: Three variants side by side
- **WHEN** the graph-depth suite runs
- **THEN** the report holds `depth-2`, `depth-3` and `depth-4`, each with the structural and the end-to-end metrics

#### Scenario: A case that needs three calls
- **WHEN** a case's deepest needed item is reached at three calls
- **THEN** `depth-2` reports it as not fully reached, and `depth-3` and `depth-4` report it as fully reached

#### Scenario: Recall by required depth
- **WHEN** the dataset holds cases that need one, two, three and four calls
- **THEN** each variant reports recall separately for each required depth as well as overall

#### Scenario: Cost is reported beside recall
- **WHEN** a deeper variant reaches more nodes
- **THEN** the report shows its larger mean node count, token size and truncated rate next to its recall

#### Scenario: Structural only
- **WHEN** the suite runs with the structural-only option and no chat model key is set
- **THEN** it reports the structural metrics for all three variants and does not start a chat turn

#### Scenario: The model skips the graph
- **WHEN** the agent answers a case without calling a graph tool
- **THEN** the case is still judged, and the variant's tool-call share is lower

### Requirement: Comparison suites do not gate
A suite whose purpose is to compare settings, starting with `graph-depth`, SHALL be reported in the same JSON and
Markdown shape as every other suite, but SHALL NOT gate:
- it has no thresholds;
- it is not compared with the baseline;
- asking to accept a baseline SHALL leave its entry absent;
- running all suites SHALL NOT include it, and it SHALL run only when named.

Its run SHALL fail only when it could not run: an invalid dataset, a tool or server that could not be reached, or a
pin the server refused.

#### Scenario: Not in all
- **WHEN** the harness runs with `all` suites
- **THEN** graph-depth does not run

#### Scenario: Never in the baseline
- **WHEN** graph-depth runs with the baseline accept option
- **THEN** the baseline file has no graph-depth entry afterwards

#### Scenario: A worse number does not fail
- **WHEN** a variant's recall is lower than in the previous run
- **THEN** the run still passes, and the report shows the numbers

#### Scenario: It cannot run
- **WHEN** the code graph server cannot be reached
- **THEN** the run fails and says which variant could not start

### Requirement: Graph depth progress
The graph-depth suite SHALL show one determinate progress bar over every case of every variant it runs. The bar SHALL
read done/total and a percentage, and SHALL follow the progress-feedback rules for output that is not a terminal and
for the final line. Progress lines SHALL carry case ids and counts, never a question or an answer.

#### Scenario: A terminal run
- **WHEN** a developer runs `make eval SUITE=graph-depth` in a terminal
- **THEN** one bar advances with each finished case of each variant and reads done/total and a percentage

#### Scenario: Output redirected
- **WHEN** the output is not a terminal
- **THEN** the suite prints plain progress lines at a bounded rate, with no control characters
