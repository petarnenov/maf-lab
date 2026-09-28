# Spec Delta

## ADDED Requirements

### Requirement: Relevance gate on the fused candidates
Search SHALL be able to ask an external relevance judge, once per search, whether each of the first fused candidates
(at most a configured number) addresses the subject of the query. When the gate is on and the judge answers, a search
whose highest relevance probability is below a configured relevance floor SHALL return no results; a search where any
judged candidate reaches the floor SHALL return the fused candidates unchanged in content and order. The gate SHALL
decide whether the corpus answers a query, never which of the candidates are returned.

The judge SHALL receive only candidates the tenant-scoped search already returned for the caller, and the query as
searched; both SHALL be carried as data, never as instructions. The gate SHALL be switchable by configuration, and its
floor, candidate count and time budget SHALL be configurable.

A judge that does not answer within its time budget, rejects the request, answers without a usable probability, or
has no credential SHALL leave the search as it would be without the gate, and SHALL NOT fail it. The reason SHALL be
recorded in structured logs without the query or passage text, and in diagnostics when they are requested.

#### Scenario: A question the corpus cannot answer
- **WHEN** the gate is on and every judged candidate's relevance probability is below the floor
- **THEN** the search returns no results

#### Scenario: A question the corpus answers
- **WHEN** the gate is on and at least one judged candidate reaches the floor
- **THEN** the search returns the same candidates in the same order as it would with the gate off

#### Scenario: The judge is slow or down
- **WHEN** the gate is on and the judge times out, returns an error status, or no credential is configured
- **THEN** the search returns the fused candidates within the judge's time budget plus the search's own time, and the reason is recorded without content

#### Scenario: Gate switched off
- **WHEN** the gate is configured off and no Jev reranker is selected
- **THEN** no relevance request is made and search behaves as it did before the gate existed

#### Scenario: The judge sees only the caller's candidates
- **WHEN** a gated search runs for a user of one firm
- **THEN** every passage sent to the judge belongs to that firm or to shared content

## MODIFIED Requirements

### Requirement: Optional rerank
An optional rerank stage SHALL be switchable by configuration, and the reranker SHALL be selectable: a language-model
listwise reranker, or a reranker that orders candidates by the relevance judge's per-candidate probabilities, highest
first, with equal probabilities keeping their fused order. When both the relevance gate and the judge-based reranker
are on, they SHALL share one relevance request per search. When the reranker is unavailable the stage SHALL behave as
a no-op without failing the search, and the judge-based reranker SHALL give up within the judge's time budget.
Reranker input SHALL contain only the caller's tenant-scoped candidates.

#### Scenario: Reranker unavailable
- **WHEN** rerank is enabled but the rerank model cannot be reached
- **THEN** search returns fused results unchanged and records the degradation in structured logs

#### Scenario: Judge-based rerank
- **WHEN** rerank is enabled with the judge-based reranker and the judge answers
- **THEN** the results are ordered by the judge's probabilities, highest first, and candidates the judge scored equally keep their fused order

#### Scenario: One relevance request per search
- **WHEN** the relevance gate and the judge-based reranker are both on
- **THEN** exactly one relevance request is made for the search, and the gate and the order are derived from the same answers
