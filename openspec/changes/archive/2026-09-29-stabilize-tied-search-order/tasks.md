# Tasks

## 1. Deterministic order in the query method

- [x] 1.1 Add `internal static IReadOnlyList<ScoredChunk> Settle(IEnumerable<ScoredChunk> results, int limit)` to `TenantScopedSearch` (score descending, chunk id ascending with ordinal comparison, then trimmed to `limit`) and a `TieMargin` constant of 10; verify with unit tests: equal scores ordered by chunk id whatever the input order, the tie at the limit keeps the lowest chunk id, higher scores always come first, fewer results than the limit are returned whole
- [x] 1.2 In `QueryAsync`, ask the store for `Limit + TieMargin` in every mode and return `Settle(mapped, Limit)`; verify `QueryPathEnumerationTests` and `RelevanceFloorTests` still pass and the solution builds with `-warnaserror`

## 2. Verification

- [x] 2.1 Run the full unit suite (`dotnet test --project tests/Maf.Lab.Tests`) and `openspec validate --all --strict`; both green
- [x] 2.2 Confirm on CI that `.NET build and tests` (which runs `RelevanceGateAcceptanceTests` against a real Qdrant) passes on both the push and the pull_request run
