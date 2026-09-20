# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
