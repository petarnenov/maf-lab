# Proposal

## Why

`RelevanceGateAcceptanceTests.A_judge_that_does_not_answer_leaves_the_search_ungated` fails intermittently in CI.
It failed on three of the last four `main` runs, and passed on one with no fix. It ranks the same query twice, gated
with a judge that times out and ungated, and expects the same 20 chunks. In CI the two lists sometimes differ at
index 13. Locally the test and the whole integration project pass every time.

The gate path does not reorder anything, so the difference comes from Qdrant: two identical queries in one process
returned different tails. The corpus has many exactly tied BM25 scores, including a group of four at ranks 95–98,
next to the prefetch limit of 100. Which tied chunk is kept, and in what order, is not guaranteed across calls. A
first attempt waited for the collection to settle after indexing (Green, optimizer idle). CI still failed the same
way, so that was not the cause, and the wait was taken out again.

## What Changes

- The test compares the gated ranking with the fused candidates of **the same** search, which the search's
  diagnostics already record, instead of with a second search. That is exactly its claim: a judge that does not
  answer leaves the search as fusion produced it. A gate that emptied or reordered the list still fails it.
- With that fixed, CI showed the next test sharing the process: the `AgentMcpIntegrationTests` span tests listen to
  every `maf-lab` activity in the process, and a search from a test running alongside put its `retrieval.*` spans
  under another trace. Both tests now send their own trace id and check only the spans in that trace. They collect
  spans in a thread-safe queue, since spans stop on many threads.
- No product code changes.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
<!-- None (skip_specs): a test's assertion only. -->

## Impact

- `tests/Maf.Lab.IntegrationTests/RelevanceGateAcceptanceTests.cs`, `AgentMcpIntegrationTests.cs`.
- Ties in the ranking can order differently between identical queries. That is a property of the product, and it
  is noted here rather than changed. The neighbouring test that compares two searches' top 10 has not failed, and it
  is left as it is.
