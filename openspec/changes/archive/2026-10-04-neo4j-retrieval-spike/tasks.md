# Tasks

## 1. Store

- [x] 1.1 Add graph source `retrieval`, the `RetrievalChunk` and `Term` labels and the `IN` relation. Add the vector
      index (768, cosine; filter properties `tenant_id`, `source_type`, `collection`) and the `Term.key` index to
      `EnsureSchemaAsync`. Verify with an integration test: both indexes exist after the schema step on
      `neo4j:2026.09.0-community`.
- [x] 1.2 Extend the maintenance scroll to return dense and sparse vectors when asked. Add the `neo4j-chunks` indexer
      command and `make neo4j-chunks`: a determinate progress bar, writes through `TenantScopedGraphMaintenance`
      (idempotent, stale removal), billing and portfolio collections, and a reported time and store size. Verify with
      an integration test (Qdrant and Neo4j Testcontainers): copied chunk count, payload fields and term weights equal
      the source, a second run writes nothing, and one chunk's stored dense vector matches the source.

## 2. Search

- [x] 2.1 Add the `retrieval_dense` (with and without source types) and `retrieval_sparse` templates (D3), and teach
      the template guard the `SEARCH … WHERE` form. Verify that the guard test passes for them and still fails for a
      fixture template without the tenant predicate.
- [x] 2.2 Extract `IChunkSearch` from `TenantScopedSearch`, make `DocumentSearchService` depend on it, and keep service
      registration Qdrant-only. Verify that the existing unit and integration tests pass unchanged, and add a test that
      the api and the three MCP servers resolve no graph chunk search.
- [x] 2.3 Implement `GraphChunkSearch`: two branches through `IGraphReader`, 5× fetch, per-branch floors, cosine
      conversion (D4), and RRF/DBSF fusion (D5), with the RRF constant taken from Qdrant's documentation. Verify with
      unit tests: the conversion, floors per branch, an empty result when both branches are empty, RRF and DBSF over
      fixed lists, and a test that fuses the same two lists as Qdrant and asserts the same order.
- [x] 2.4 Port the small-tenant and cross-tenant acceptance tests to the Neo4j backend (Neo4j Testcontainers). Verify:
      a small firm gets its full count beside a 10× larger firm, and firm B's chunks are never candidates for firm A.

## 3. Eval

- [x] 3.1 Add the `retrieval-backends` comparison suite (D7) and `make eval-retrieval-backends`, with the count check
      and one progress bar. Verify with unit tests for overlap@5 and the refusal on a count mismatch, and that it is not
      in `all` and never accepted into the baseline.
- [x] 3.2 Run `make neo4j-chunks`, then `make eval-retrieval-backends` twice. Verify that both reports are in
      `evals/reports/`, and record per backend: recall@5/@20, MRR, off-domain silence per language, overlap@5,
      latency p50/p95, and copy time and size.

## 4. Decision and documentation

- [x] 4.1 Write the DECISIONS.md section (D8) with the numbers from 3.2 and the small-tenant result, ending in one
      outcome. Verify that it cites the two reports by name.
- [x] 4.2 README: add the `retrieval-backends` line to "Evals — when you must run them" (comparison, not gated). Run
      `make docs` and `make docs-check`. Verify that both pass.
