# Spec Delta

## MODIFIED Requirements

### Requirement: Retrieval diagnostics on request
When a `search_documents` request carries the `maf-lab/trace` flag in its `_meta`, the result SHALL include a
diagnostics object in the result `_meta` with:
- the serving replica and the tenant scope applied (tenant ids only);
- the effective settings: mode, fusion, dense vector, prefetch and final limits, rerank;
- the BM25 query terms, each marked as being in the indexed vocabulary or not, with its IDF weight when it is in
  the vocabulary and no weight when it is not, and the dense model and dimensions;
- the dense-only, sparse-only and fused candidate lists (chunk id, doc id, tenant, score, rank), plus the rerank order
  when rerank is on;
- the embedding and vector-store timings.

A term the indexed corpus has never seen has no IDF weight and cannot contribute to sparse matching. Diagnostics
SHALL report such a term rather than dropping it, and SHALL NOT invent a weight — neither zero nor any other
number — for it. A consumer SHALL be able to tell the two cases apart from the diagnostics alone.

Diagnostics MUST contain only candidates within the caller's tenant scope. They MUST NOT be part of the structured
content or text content the model receives. Without the flag, results SHALL NOT include diagnostics.

#### Scenario: Diagnostics requested
- **WHEN** the agent calls `search_documents` with the trace flag
- **THEN** the result `_meta` contains diagnostics whose candidates all belong to the caller's firm or shared, and the structured content is identical to a call without the flag

#### Scenario: A term the corpus has never seen
- **WHEN** a traced query contains a term that is not in the indexed BM25 vocabulary
- **THEN** the diagnostics list that term, mark it as outside the vocabulary, and give it no IDF weight

#### Scenario: A term the corpus knows
- **WHEN** a traced query contains a term that is in the indexed BM25 vocabulary
- **THEN** the diagnostics list that term, mark it as in the vocabulary, and give its IDF weight

#### Scenario: Not requested
- **WHEN** a client calls `search_documents` without the flag
- **THEN** the result has no diagnostics in `_meta`

#### Scenario: Model never sees diagnostics
- **WHEN** the agent passes a traced search result to the model
- **THEN** the data envelope contains the structured content only, with no diagnostics
