# hybrid-retrieval Specification

## Purpose
Defines how tenant-scoped search behaves over the shared vector store:
hybrid dense+sparse retrieval with fusion, comparable modes, optional
reranking, and per-tenant recall that does not degrade for small firms.

## Requirements

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

Each candidate branch SHALL apply its own relevance floor, so that a candidate too far from the query never
reaches fusion. The floors SHALL be per branch and SHALL be configurable, including being switched off. A floor
SHALL NOT be applied to the fused score: a reciprocal-rank-fusion score expresses rank and the number of
candidates fused, not closeness to the query, so it cannot carry a meaning a floor could test. Dense and sparse
scores are on different scales and SHALL have independent floors.

A search whose branches all come back empty SHALL return no results, rather than the nearest candidates the store
happens to hold. The retrieval path SHALL therefore be able to produce an empty result for a query it simply
cannot answer.

#### Scenario: Default fusion
- **WHEN** a search is executed with default configuration
- **THEN** results are the RRF fusion of dense and sparse candidates

#### Scenario: Tenant filter on every branch
- **WHEN** any search is executed
- **THEN** both candidate branches and the final result are restricted to the caller's firm and shared content

#### Scenario: A candidate too far from the query
- **WHEN** a branch's nearest candidates all score below that branch's floor
- **THEN** that branch contributes nothing to fusion

#### Scenario: Nothing close in either branch
- **WHEN** neither branch has a candidate above its floor
- **THEN** the search returns no results at all

#### Scenario: The floors are independent
- **WHEN** the dense and sparse floors are configured
- **THEN** each is applied to its own branch's scores, and neither is applied to the fused score

#### Scenario: Floors switched off
- **WHEN** the floors are configured off
- **THEN** search behaves as it did before they existed, returning the nearest candidates whatever their scores

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

### Requirement: Query language normalisation
A search query that is not written in the corpus's language SHALL be translated into it before the query is
embedded and before sparse encoding, so that dense and sparse retrieval both work on text drawn from the same
vocabulary as the indexed chunks. The corpus language and whether normalisation runs at all SHALL be configuration.

Normalisation SHALL preserve the meaning of the question and SHALL keep identifiers, codes and product terms as
they are written (for example `FS-REQUIRED`, a run id, a file name). It SHALL NOT change what the user is told:
only the text used to search is affected.

A query already in the corpus's language SHALL be used unchanged, without contacting a model. Translation that
fails, times out or returns nothing usable SHALL leave the original query in place, and the search SHALL proceed —
never fail — exactly as it does today. Results of the same translation MAY be reused within a run.

The tenant scope and the single tenant-scoped query path SHALL be unaffected: normalisation changes only the text
of the query, never who may see what.

#### Scenario: A question in another language finds the same documents
- **WHEN** a user asks "каква е процедурата когато липсва фий схема" over an English corpus
- **THEN** the search returns the same documents as the English question "what is the procedure when a fee schedule is missing"

#### Scenario: An English query is not translated
- **WHEN** the query is already in the corpus's language
- **THEN** no translation is attempted and the query is searched as written

#### Scenario: Identifiers survive
- **WHEN** a question in another language mentions `FS-REQUIRED` or run 4417
- **THEN** those tokens appear unchanged in the query that is searched

#### Scenario: Translation is unavailable
- **WHEN** the translation model fails or exceeds its budget
- **THEN** the original query is searched, the search still returns results, and the reason is recorded in the diagnostics

#### Scenario: Normalisation is switched off
- **WHEN** query normalisation is disabled by configuration
- **THEN** every query is searched exactly as written

#### Scenario: The monitor shows what was searched
- **WHEN** a query was normalised
- **THEN** the retrieval diagnostics carry both the original and the searched query
