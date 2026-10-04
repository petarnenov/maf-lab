# Spec Delta

## ADDED Requirements

### Requirement: Deterministic result order
Identical searches over an unchanged index SHALL return the same results in the same order, in every retrieval mode.
Results SHALL be ordered by score, highest first. Results with equal scores SHALL be ordered by chunk id, ascending,
so that the fused order the rerankers rely on is well defined. When candidates with equal scores straddle the
requested result count, which of them are returned SHALL be decided by that same order, never by the vector store's
internal ordering of ties.

#### Scenario: Equal fused scores
- **WHEN** two candidates reach the same reciprocal-rank-fusion score, one found only by the dense branch at rank 3 and one found only by the sparse branch at rank 3
- **THEN** the one with the lower chunk id is returned first, on every repetition of the search

#### Scenario: Repeated search
- **WHEN** the same query is searched twice with the same settings and scope over an unchanged index
- **THEN** both searches return the same chunk ids in the same order

#### Scenario: Tie at the result limit
- **WHEN** the last result position is contested by candidates with equal scores
- **THEN** the candidate with the lowest chunk id among them is returned, and the others are not, on every repetition of the search
