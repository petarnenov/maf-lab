# Spec Delta

## Purpose

Defines how tenant-scoped search behaves over the shared vector store:
hybrid dense+sparse retrieval with fusion, comparable modes, optional
reranking, and per-tenant recall that does not degrade for small firms.

## ADDED Requirements

### Requirement: Single shared collection with per-tenant indexing
All tenants SHALL share one collection using payload-based multitenancy, with
the tenant field indexed as a tenant key so each tenant has its own search
graph. Source type, doc_id, updated_at and model_version SHALL be indexed for
filtering.

#### Scenario: Collection bootstrap
- **WHEN** the system starts against an empty vector store
- **THEN** the collection is created with dense and sparse named vectors, the tenant-key index, and the listed payload indexes

### Requirement: Hybrid search with server-side fusion
Search SHALL run a dense and a sparse candidate retrieval, each restricted to
the caller's tenant scope, and fuse them in the vector store. Reciprocal rank
fusion SHALL be the default and distribution-based fusion SHALL be selectable
by configuration. Each candidate branch SHALL fetch at least five times the
requested result count.

#### Scenario: Default fusion
- **WHEN** a search is executed with default configuration
- **THEN** results are the RRF fusion of dense and sparse candidates

#### Scenario: Tenant filter on every branch
- **WHEN** any search is executed
- **THEN** both candidate branches and the final result are restricted to the caller's firm and shared content

### Requirement: Selectable retrieval modes
The retrieval stack SHALL support hybrid, dense-only, and sparse-only modes,
selectable by configuration, for evaluation comparison.

#### Scenario: Dense-only
- **WHEN** the mode is set to dense-only
- **THEN** search uses only the dense embedding and still applies the tenant filter

### Requirement: Optional rerank
An optional rerank stage SHALL be switchable by configuration. When the
reranker is unavailable the stage SHALL behave as a no-op without failing the
search. Reranker input SHALL contain only the caller's tenant-scoped candidates.

#### Scenario: Reranker unavailable
- **WHEN** rerank is enabled but the rerank model cannot be reached
- **THEN** search returns fused results unchanged and records the degradation in structured logs

### Requirement: No post-filter shrinkage for small tenants
A tenant SHALL receive its full requested result count whenever it has at least
that many matching chunks, regardless of how many closer matches exist in other
tenants.

#### Scenario: Small tenant beside a large one
- **WHEN** a firm with 50 documents searches for a term whose global nearest neighbours are all in the 10x larger firm
- **THEN** it receives 10 results from its own content and shared content

#### Scenario: Full count under competition
- **WHEN** firm A queries text that matches many firm B documents
- **THEN** firm A's result count equals the requested limit (given enough firm A/shared matches)
