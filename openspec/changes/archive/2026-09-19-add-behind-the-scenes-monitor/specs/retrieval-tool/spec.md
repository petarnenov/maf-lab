# Spec Delta

## ADDED Requirements

### Requirement: Retrieval diagnostics on request
When a `search_documents` request carries the `maf-lab/trace` flag in its `_meta`, the result SHALL include a
diagnostics object in the result `_meta` with:
- the serving replica and the tenant scope applied (tenant ids only);
- the effective settings: mode, fusion, dense vector, prefetch and final limits, rerank;
- the BM25 query terms with their IDF weights, and the dense model and dimensions;
- the dense-only, sparse-only and fused candidate lists (chunk id, doc id, tenant, score, rank), plus the rerank order
  when rerank is on;
- the embedding and vector-store timings.

Diagnostics MUST contain only candidates within the caller's tenant scope. They MUST NOT be part of the structured
content or text content the model receives. Without the flag, results SHALL NOT include diagnostics.

#### Scenario: Diagnostics requested
- **WHEN** the agent calls `search_documents` with the trace flag
- **THEN** the result `_meta` contains diagnostics whose candidates all belong to the caller's firm or shared, and the structured content is identical to a call without the flag

#### Scenario: Not requested
- **WHEN** a client calls `search_documents` without the flag
- **THEN** the result has no diagnostics in `_meta`

#### Scenario: Model never sees diagnostics
- **WHEN** the agent passes a traced search result to the model
- **THEN** the data envelope contains the structured content only, with no diagnostics
