# Spec Delta

## MODIFIED Requirements

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
