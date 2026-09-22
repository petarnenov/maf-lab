# Spec Delta

## MODIFIED Requirements

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
