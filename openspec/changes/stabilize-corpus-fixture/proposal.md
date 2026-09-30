# Proposal

## Why

CI has failed on `main` since `add-codebase-search`, always on
`RelevanceGateAcceptanceTests.A_judge_that_does_not_answer_leaves_the_search_ungated`. The test ranks the same query
twice, gated with a failing judge and ungated, and expects the same 20 chunks. In CI the two lists differ at index 13.
Locally the test and the whole integration project pass every time.

The gate path does not change the ranking. The test's two identical queries got different answers from Qdrant:
- The indexed corpus (3,330 points since the codebase corpus grew it) has many exactly tied BM25 scores, including a
  group of four at ranks 95–98, next to the prefetch limit of 100.
- Which tied chunk is kept is decided by Qdrant's segment layout. The optimizer keeps changing that layout for a
  while after indexing.
- On a fast machine the collection is already Green when the tests start. On a CI runner it may still be optimizing
  between the two queries.

## What Changes

- `CorpusIndexFixture` waits after indexing until the collection is Green with the optimizer idle, bounded at 120 s,
  and fails with a clear message if it never gets there. It logs how long it waited and the status it started from,
  so a CI log confirms the cause.
- No product code changes. The ranking and its tie handling are unchanged.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
<!-- None (skip_specs): test infrastructure only. -->

## Impact

- `tests/Maf.Lab.IntegrationTests/CorpusIndexFixture.cs`. Every test in the `indexed-corpus` collection starts from a
  settled index.
