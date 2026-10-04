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
scores are on different scales and SHALL have independent floors. The dense floor SHALL belong to the dense
embedding it was calibrated for: selecting a different dense embedding SHALL select that embedding's floor, never
carry over another's, because different embedding models place the same closeness at different scores.

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

#### Scenario: Switching the dense embedding switches its floor
- **WHEN** the dense branch is configured to use a different embedding
- **THEN** the floor applied to dense candidates is the one calibrated for that embedding, and a search for an answerable question still returns results

### Requirement: Selectable retrieval modes
The retrieval stack SHALL support hybrid, dense-only, and sparse-only modes,
selectable by configuration, for evaluation comparison.

#### Scenario: Dense-only
- **WHEN** the mode is set to dense-only
- **THEN** search uses only the dense embedding and still applies the tenant filter

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

### Requirement: Multilingual dense retrieval
The dense branch SHALL use an embedding model trained for many languages, so that a question finds the documents that
answer it whether it is written in English, in Bulgarian, or in Bulgarian written in Latin letters — including when
query normalisation does not translate it. The model SHALL be chosen by the retrieval eval, and the choice and the
numbers behind it SHALL be recorded.

#### Scenario: Latin-script Bulgarian finds the documents
- **WHEN** a user asks "Kakva e procedurata kogato lipsva fee schedule?"
- **THEN** the dense branch returns the missing-fee-schedule procedure among its candidates, although the query was not translated

#### Scenario: English does not get worse
- **WHEN** the retrieval eval runs after the change
- **THEN** English recall@5 is at or above its accepted baseline

#### Scenario: One dense embedding in production
- **WHEN** the change is complete
- **THEN** the collection holds exactly one dense embedding, the chosen multilingual model's, and every search uses it with its own floor

#### Scenario: Rollback
- **WHEN** the previous dense embedding's profile is configured again, selected, and the collection is rebuilt
- **THEN** searches use it and its own floor, with no code change

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

### Requirement: Experimental Neo4j search backend
For measurement only, the retrieval library SHALL provide a second implementation of the tenant-scoped chunk search that
reads chunks stored in Neo4j. It SHALL be constructed only by the eval harness and SHALL NOT be registered or selectable
in any deployed service.

It SHALL honour the same contract as the Qdrant search:
- the same request;
- a dense and a sparse branch, each fetching at least five times the requested count;
- each branch restricted to the caller's readable tenants;
- the same per-branch floors, on the same score scales;
- reciprocal rank fusion by default, and distribution-based fusion when selected;
- an empty result when every branch is empty.

The tenant restriction SHALL be applied inside each branch's search, not to its results, so that a small tenant is not
shortchanged by a large one. Its reads SHALL go only through the tenant-scoped graph read path with fixed templates.

#### Scenario: Never deployed
- **WHEN** the api, the retrieval, portfolio and code MCP servers start with any configuration
- **THEN** none of them resolves the Neo4j search backend

#### Scenario: Small tenant beside a large one, on Neo4j
- **WHEN** a firm with few chunks searches a term whose global nearest neighbours all belong to a ten times larger firm
- **THEN** the Neo4j backend returns the requested count from the firm's own and shared chunks

#### Scenario: Another firm's chunk
- **WHEN** firm A searches on the Neo4j backend
- **THEN** no chunk of firm B is a candidate in either branch

#### Scenario: Same sparse scores
- **WHEN** a chunk is found by the sparse branch on both backends for the same query
- **THEN** its sparse score is the same, because both compute the same BM25 dot product

#### Scenario: Same dense scale
- **WHEN** a dense floor calibrated on Qdrant's cosine scores is applied on the Neo4j backend
- **THEN** it compares against the same cosine value, converted from Neo4j's normalised score
