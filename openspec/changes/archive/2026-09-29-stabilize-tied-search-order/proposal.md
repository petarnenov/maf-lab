# Proposal

## Why

`RelevanceGateAcceptanceTests.A_judge_that_does_not_answer_leaves_the_search_ungated` failed on CI (push run
36561799371, commit `e828c3b`). The same commit passed on the `pull_request` run, and `main` has had similar sporadic
.NET failures before. The test runs the same search twice and compares the order: at position 13 of 20 the two
searches disagreed.

The test is not what is wrong; the search is. A reciprocal-rank-fusion score depends only on ranks, so ties are
built in:
- a candidate at dense rank *r* that the sparse branch did not return scores exactly the same as a candidate at
  sparse rank *r* that the dense branch did not return;
- a candidate at ranks (*i*, *j*) scores the same as one at (*j*, *i*).

The vector store returns tied points in no guaranteed order. It also does not guarantee which of the tied points
survives the result limit. So the same query can return a different order, or a different last result, from one
call to the next. That breaks what the spec already assumes when it says a reranker keeps equal probabilities "in
their fused order": that order is not well defined today.

## What Changes

- Search results with equal scores are always returned in the same order: score descending, then chunk id ascending.
  This applies in every mode (hybrid, dense, sparse).
- The cut at the requested result count becomes deterministic as well. The one query method asks the store for a few
  more points than requested, orders them, and keeps the requested number. The fused scores do not change: RRF
  depends on the candidate branches, not on the outer limit.
- The query path stays one method, and the tenant filter is unchanged. The ordering is a pure function applied to
  that method's result.
- The acceptance test that failed stays as written: once search is deterministic, it holds. A unit test pins the
  ordering and the cut.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `hybrid-retrieval`: adds a requirement that search results are deterministic — equal scores are ordered by chunk id,
  and which tied candidates are returned at the result limit does not vary between identical searches.

## Impact

- Code: `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs` (over-fetch by a small margin, order, trim).
- Tests: new unit tests for the ordering; `RelevanceGateAcceptanceTests` unchanged.
- No API, contract, configuration or package change. Result order changes only among candidates with equal scores.
