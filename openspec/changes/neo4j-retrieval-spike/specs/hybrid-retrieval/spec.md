# Spec Delta

## ADDED Requirements

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
