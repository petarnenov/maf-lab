# Spec Delta

## Purpose

Measures tool selection, retrieval quality, answer quality, and injection
resistance on demand, so that changes to prompts, tools, models, or chunking
are judged by numbers rather than impressions.

## ADDED Requirements

### Requirement: JSONL datasets
The harness SHALL read datasets from `evals/`: `selection.jsonl` (question,
expectedTools — empty means no tool), `retrieval.jsonl` (query,
relevantChunkIds), `generation.jsonl` (question, reference answer, expected
source docIds), and `injection.jsonl` (question, forbidden strings, forbidden
tenant ids).

#### Scenario: Selection coverage
- **WHEN** the selection dataset is loaded
- **THEN** it contains obvious-docs, obvious-data, boundary (two tools), and negative cases

### Requirement: Metrics
The harness SHALL compute selection recall and precision; retrieval recall@5,
recall@20 and MRR; generation faithfulness and answer relevance by an
LLM judge with a fixed rubric; and injection pass rate.

#### Scenario: Selection expectations
- **WHEN** the selection eval runs
- **THEN** "what is the procedure when a fee schedule is missing" expects `search_documents`, "status of run 4417" expects `get_billing_run_status`, "why did run 4417 fail" expects both, and "thanks, that's all" expects none

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

#### Scenario: Threshold failure
- **WHEN** a metric is below its configured threshold
- **THEN** the report marks that suite as failed and the command exits non-zero

### Requirement: Feedback import
The harness SHALL import labeled feedback rows so that the next run includes
them.

#### Scenario: Wrong-document feedback
- **WHEN** a user flagged "wrong document" and a reviewer labeled it
- **THEN** the next retrieval eval run includes that row
