# Proposal

## Why

The question is whether maf-lab could keep Neo4j alone and drop Qdrant. One store instead of two, graph and search in
one query, and one tenant-scoped read path instead of two. Today that cannot be answered from facts.

Neo4j 2026.09 has what a migration would need on paper:
- vector indexes with in-index filtering through the Cypher 25 `SEARCH` clause (GA since 2026.02, Community included,
  `IN` since 2026.06).

It also lacks things Qdrant gives us:
- sparse vectors;
- fusion in the store;
- in-index filtering for full-text indexes.

The project's retrieval quality, tenant guarantees ("No post-filter shrinkage for small tenants") and floors were all
measured on Qdrant. A migration decided without numbers would put all of that at risk. This spike measures first.

## What Changes

- **An experimental Neo4j search backend.** It is a second implementation of the tenant-scoped chunk search over the
  same chunks, built only by the eval harness and never registered in a deployed service. Qdrant stays the store of
  every service.
  - **Dense branch:** the chunk's `dense_v3` vector in a Neo4j vector index, queried with `SEARCH … WHERE
    c.tenant_id IN $readable`, with the filter properties declared on the index.
  - **Lexical branch:** our own BM25 kept as a graph inverted index (`(:Term)-[:IN {w}]->(:Chunk)`), using the same
    term ids, weights and query encoder as the Qdrant sparse vector. The tenant filter is part of the `MATCH`, so it is
    exact, and the sparse score is the same dot product.
  - **The rest:** per-branch floors, five-times candidate fetch, and RRF/DBSF fusion in code. Reads go only through
    `TenantScopedGraph.ReadAsync` with fixed templates.
- **A copy command.** `make neo4j-chunks` copies the billing and portfolio chunks from Qdrant into Neo4j, with their
  payloads, dense vectors and sparse vectors, so both backends search identical data. It is idempotent and shows a
  progress bar. Code chunks are left out of this spike: they have no labelled retrieval cases.
- **A comparison suite.** `retrieval-backends` runs the retrieval cases on both backends side by side. It reports:
  - recall@5, recall@20, MRR and off-domain silence, per language and per domain;
  - rank agreement with Qdrant (overlap@5);
  - query latency p50/p95.

  It never gates, is never baselined, and is not part of `all`. `make eval-retrieval-backends`.
- **A small-tenant test on Neo4j.** A Neo4j port of the small-tenant acceptance test: a firm with few chunks beside a
  large one still gets its full requested count.
- **Copy cost.** The copy command reports build time and store size for each backend.
- **A decision record.** It goes in DECISIONS.md, with the numbers: migrate, do not migrate, or what would have to
  change first. No migration is part of this change.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `hybrid-retrieval`: adds the experimental Neo4j backend's requirements: never deployed, same tenant guarantees, same
  floors, fusion in code.
- `eval-harness`: adds the `retrieval-backends` comparison suite.

## Impact

- `src/Maf.Lab.Retrieval/Graph`:
  - templates for the chunk vector search, the term search and the chunk copy;
  - the vector and fulltext schema;
  - a `GraphChunkSearch` with the same contract as `TenantScopedSearch.QueryAsync`.
- `src/Maf.Lab.Retrieval/Search`: `DocumentSearchService` takes its chunk search through an interface, so the eval can
  hand it the Neo4j one. Production wiring still resolves Qdrant's.
- `src/Maf.Lab.Indexing`: the `neo4j-chunks` command, reading Qdrant through the existing maintenance scroll and writing
  through `TenantScopedGraphMaintenance`.
- `src/Maf.Lab.Eval`: the `retrieval-backends` suite.
- `tests`:
  - the template guard extended to the new templates;
  - unit tests for fusion and floors;
  - Neo4j integration tests for tenancy and the small tenant.
- `Makefile`: `neo4j-chunks` and `eval-retrieval-backends`.
- No package change: `Neo4j.Driver` and `Qdrant.Client` are already referenced. No compose change.
- **Progress.** The copy command and the suite each show a determinate progress bar (progress-feedback).

## Documentation impact

- `README.md`:
  - the make-targets block, through `make docs`;
  - a line in "Evals — when you must run them" naming `retrieval-backends` as a comparison.
- `DECISIONS.md`: the spike's decision record, with its numbers. It is history, not checked by docs-check.
- `CLAUDE.md`, `openspec/project.md`: unchanged. Qdrant remains the vector store, and the experiment is eval-only.
