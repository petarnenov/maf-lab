# Spec Delta

## ADDED Requirements

### Requirement: Retrieval backends comparison
A `retrieval-backends` suite SHALL run the retrieval cases on the Qdrant backend and on the experimental Neo4j backend,
over the same copied chunks, with the same search settings. It SHALL report for each backend:
- recall@5, recall@20 and MRR over the answerable cases;
- the off-domain silence over the off-domain cases;
- each of these per query language;
- query latency at p50 and p95.

For the Neo4j backend it SHALL also report the share of the top five that Qdrant also ranks in its top five.

It SHALL be a comparison suite: no thresholds, never compared with or accepted into the baseline, and not part of
`all`. It SHALL refuse to run when the Neo4j copy of the chunks is missing or does not match the Qdrant chunk count of a
collection, rather than report numbers over different data. It SHALL show one determinate progress bar over cases ×
backends.

#### Scenario: Side by side
- **WHEN** the suite runs
- **THEN** the report holds a Qdrant variant and a Neo4j variant with the same metrics, and the Neo4j variant also reports overlap@5 with Qdrant

#### Scenario: Stale copy
- **WHEN** the Neo4j copy holds fewer chunks than the Qdrant collection
- **THEN** the suite stops before scoring and says which collection is out of step

#### Scenario: Not gated
- **WHEN** the Neo4j variant scores below the Qdrant variant
- **THEN** the run still passes, and nothing is written to the baseline
