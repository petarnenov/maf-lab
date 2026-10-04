# Design

## Context

See proposal.md for why. What the spike builds on:

- **`TenantScopedSearch.QueryAsync`** (`src/Maf.Lab.Retrieval/Store`) is the one method that queries Qdrant. It builds a
  `Plan`: dense and sparse `PrefetchQuery` branches, each with the tenant `Filter`, its floor and `Limit` of at least 5×,
  fused server-side with `Fusion.Rrf` or `Fusion.Dbsf`.
- **`DocumentSearchService.RankCoreAsync`** calls it, after embedding the query (`IDenseEncoder`) and BM25-encoding it
  (`Bm25Model`, our tokenizer and IDF, vocabulary in a meta collection).
- **Per-tenant HNSW.** The collections use `M = 0, PayloadM = …`, which gives each tenant its own HNSW graph. That is
  what meets "No post-filter shrinkage for small tenants".
- **The collections:** `maf_chunks` (3330 points) and `maf_portfolio_chunks` (142), each with `dense_v3` (768,
  Cosine) and sparse `bm25`.
- **Neo4j.** It is 2026.09 Community and already holds the billing and code graphs. Every read goes through
  `TenantScopedGraph.ReadAsync` with fixed templates, and every write through `TenantScopedGraphMaintenance`. A guard
  test parses every template for the tenant predicate.
- **`SEARCH` in Neo4j.**
  - Vector indexes accept `SEARCH … WHERE` with `=`, ranges and `IN` on properties declared on the index, with `AND`
    only.
  - `LIMIT` is a ceiling, not a guarantee.
  - Full-text indexes have no `WHERE` in `SEARCH`.

## Goals / Non-Goals

**Goals:**
- Retrieval quality, tenant behaviour, latency and cost of a Neo4j-only search, measured on the same data as Qdrant.
- A decision record that a later change can act on.

**Non-Goals:**
- **Migrating, or making Neo4j selectable in a service.**
- **Re-embedding.** The copy reuses Qdrant's vectors, so a difference can only come from the store.
- **Code chunks** (no labelled retrieval cases), **contextual retrieval** and **rerankers**. The comparison is hybrid
  vs hybrid, plus dense-only and sparse-only modes if cheap.
- **Neo4j's own full-text BM25.** It would change the scores and could not filter by tenant in the index. Its numbers
  would mix two differences: a different BM25 and post-filtering.

## Decisions

### D1. Copy from Qdrant, do not re-index
`neo4j-chunks` reads each collection through the existing maintenance scroll, extended to return vectors when asked
(it already takes `withVectors`). It writes:
- a `(:RetrievalChunk {key: chunk_id, collection, tenant_id, doc_id, source_type, …payload fields, dense: [768
  floats]})` node per point;
- a `(:Term {key: "<collection>:<term id>"})` node per sparse index;
- an `(:Term)-[:IN {w}]->(:RetrievalChunk)` relationship per sparse value.

All writes go through `TenantScopedGraphMaintenance`, under a new graph source `retrieval`, so the existing
idempotency and stale-node removal apply.

Terms are shared across tenants and carry `tenant_id = shared`, since a term id says nothing about a firm. The tenant
predicate sits on the chunk.

### D2. Two indexes
- A vector index `retrieval_chunk_dense` on `RetrievalChunk.dense`, 768 dimensions, cosine, with `tenant_id`,
  `source_type` and `collection` declared as filter properties.
- A range index on `Term.key`.

Both are created by the existing schema step (`EnsureSchemaAsync`).

### D3. Templates
Both go in `GraphTemplates`, as constant text with `CYPHER 25`. Neither interpolates a value: the limit and the
vectors are parameters.

- **`retrieval_dense`:**
  ```
  MATCH (c:RetrievalChunk) SEARCH c IN (VECTOR INDEX retrieval_chunk_dense FOR $vector
     WHERE c.collection = $collection AND c.tenant_id IN $readable [AND c.source_type IN $types] LIMIT $limit) SCORE AS s
  ```
  The source-type filter is a second template rather than string assembly.
- **`retrieval_sparse`:**
  ```
  UNWIND $terms AS t
  MATCH (:Term {key: t.key})-[r:IN]->(c:RetrievalChunk)
  WHERE c.tenant_id IN $readable AND c.collection = $collection [AND c.source_type IN $types]
  WITH c, sum(r.w * t.w) AS s
  ORDER BY s DESC LIMIT $limit
  ```

The template guard test learns the `SEARCH … WHERE` form, so that a tenant predicate inside `SEARCH` counts as
guarded.

### D4. Scores and floors
- **Dense.** Neo4j's cosine score is normalised as `(1 + cos) / 2`. `GraphChunkSearch` converts it back,
  `cos = 2s − 1`, so the dense floor calibrated on Qdrant applies unchanged. A unit test pins the conversion, and the
  copy test pins one known pair (same vector, same score).
- **Sparse.** The score is the same dot product Qdrant computes, so the floor carries over.
- **Floors apply per branch** before fusion, as on Qdrant.

### D5. Fusion in code
- **RRF.** Each candidate scores `Σ 1/(k + rank)`, with Qdrant's constant. The constant is checked against Qdrant
  during apply by fusing the same two candidate lists both ways; the test pins the result.
- **DBSF.** Each branch's scores are normalised by mean ± 3σ, then summed, as Qdrant documents it.

Fusion lives in `GraphChunkSearch`. It is not a second place where queries are built: the templates are, as before.

### D6. Eval-only override, production call kept direct (revised during apply)
`IChunkSearch.QueryAsync(Principal, SearchRequest, ct)` is implemented by `TenantScopedSearch` (still the only Qdrant
query method) and by `GraphChunkSearch`. `DocumentSearchService` still takes `TenantScopedSearch` and gains an optional
`IChunkSearch? chunkSearch` that only the eval passes. Each call site reads `chunkSearch is null ?
search.QueryAsync(…) : chunkSearch.QueryAsync(…)`. Nothing registers `IChunkSearch` or `GraphChunkSearch`, and tests
assert both.

*Why it was revised.* The first version made `DocumentSearchService` depend on the interface. That cut the code graph:
Roslyn resolves the call to `IChunkSearch.QueryAsync`, so `TenantScopedSearch.QueryAsync` lost its production callers.
`GraphIntegrationTests` failed, and graph-depth's and code-route's cases about it would have lost their chains (the
same cut as `ICodeRanker`). Keeping the direct call at the same call sites leaves the graph and its hop counts as they
were. The comparison numbers were measured with the first version, which calls the same stores with the same requests,
so they stand.

### D7. The suite
`retrieval-backends` reuses `RetrievalSuite`'s scoring. It runs each case through both services, records per-query
latency, and computes overlap@5. Before scoring, it compares each collection's chunk count on both stores and refuses
on a mismatch. It is a comparison suite (`Program.ComparisonSuites`) and has one `ConsoleProgress` bar.

### D8. The decision record
After two runs, a DECISIONS.md section states:
- the numbers;
- the small-tenant result;
- the copy and index cost;
- one of three outcomes: migrate, do not migrate, or the conditions under which a migration would make sense (for
  example, if Neo4j adds in-index filtering to full-text).

## Risks / Trade-offs

- **[The graph inverted index is our own search engine.]** About 3.5k chunks × the terms each one has makes it
  1M relationships or fewer: fine at this scale, and it is what keeps BM25 and the tenant filter exact. → Latency is
  measured. If it does not hold, that is a finding for the record, not something to tune around in the spike.
- **[Cypher 25 `SEARCH` behaviour on Community 2026.09 differs from the documentation.]** → The integration tests run
  against the same image (`neo4j:2026.09.0-community`) as the stack.
- **[The copy diverges from Qdrant.]** → The suite refuses on a count mismatch. The copy is re-run after every
  `make index`.
- **[A second implementation tempts deployment.]** → Not registered anywhere, with a test that says so. The spec
  requirement says eval-only.

## Open Questions

- Qdrant's RRF constant is not in our code. It is read from its documentation during apply and pinned by test (D5).
  This does not change the plan.
