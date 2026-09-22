# Spec Delta

## MODIFIED Requirements

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
