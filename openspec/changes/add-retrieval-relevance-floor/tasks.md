# Tasks

## 1. The floor in the query path

- [x] 1.1 Add two nullable floors to `RetrievalOptions` in
      `src/Maf.Lab.Retrieval/Configuration/Options.cs`, beside `PrefetchMultiplier` and
      `MinPrefetch`, defaulting to null (no floor). Verify `make lint` builds warnings-as-errors
      clean.
- [x] 1.2 Carry the two floors on `SearchRequest` in
      `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs` and apply them in `QueryAsync`: the dense
      floor on the dense `PrefetchQuery` and on the dense-only `QueryAsync` call, the sparse floor
      on the sparse `PrefetchQuery` and the sparse-only call, and neither on the outer fusion
      query. Verify by building; the hybrid fusion call must remain without a `scoreThreshold`.
- [x] 1.3 Extend `SearchSettings` in `src/Maf.Lab.Retrieval/Search/DocumentSearchService.cs` with
      the two floors so an eval variant can sweep them, defaulting from `RetrievalOptions`, and
      pass them into the `SearchRequest` built by `RankAsync`. Verify `make lint` passes.
- [x] 1.4 Add unit tests asserting the request built for hybrid carries a threshold on each
      prefetch branch and none on the fused query, and that null floors produce a request identical
      to today's. Verify with `make test-dotnet`.

## 2. Nothing close means nothing returned

- [x] 2.1 Confirm by test that `SearchAsync` already returns the "No matching documentation" hint
      and an empty `results[]` when `RankAsync` yields nothing, and that the call is not marked as
      an error. Add the test in `tests/Maf.Lab.Tests/` if no equivalent exists. This is the
      existing path — the task is to pin it before it becomes reachable, not to write it.
- [x] 2.2 Add an integration test in `tests/Maf.Lab.IntegrationTests/` that searches an indexed
      corpus with a question it cannot answer, with floors set high enough to reject everything,
      and asserts the tool returns no results, a refine hint, and no error. Verify with
      `make test-dotnet`.
- [x] 2.3 Add a test that the same search with floors null returns the nearest candidates as
      before, so the off switch is proven rather than assumed. Verify with `make test-dotnet`.
- [x] 2.4 Add a test through the agent path asserting a zero-result `search_documents` call sets
      `ZeroResults`, produces no sources, and yields the `zero_retrieval_results` signal — the
      first time that path has ever run. Verify with `make test-dotnet`.

## 3. Diagnostics keep the near misses

- [x] 3.1 Record the applied floors in `SearchDiagnostics.Settings` in
      `src/Maf.Lab.Retrieval/Search/SearchDiagnostics.cs`, alongside the existing limits. Verify by
      inspecting a traced search's diagnostics in a test.
- [x] 3.2 Keep the `TraceBranches` dense-only and sparse-only re-runs in `DocumentSearchService`
      unfiltered by the floors, and mark each diagnostic candidate with whether it cleared its
      branch's floor. Verify by test that a search returning nothing still reports its candidates,
      marked as dropped.
- [x] 3.3 Add a test asserting the diagnostics distinguish "candidates found, all dropped" from
      "no candidates at all". Verify with `make test-dotnet`.

## 4. The eval learns to judge silence

- [x] 4.1 Add an off-domain marker to `RetrievalCase` in `src/Maf.Lab.Eval/Datasets/Datasets.cs`
      and its loader, so a row with no relevant chunks is only valid when it declares itself
      off-domain. Verify with a loader test that an unmarked row with no relevant chunks is
      rejected.
- [x] 4.2 Add off-domain cases to `evals/retrieval.jsonl`, drawn from real traces in the store
      rather than invented — including the reported "Какво е JWE?" and "What is JWE?" queries.
      Verify the dataset loads and the suite counts them separately.
- [x] 4.3 In `src/Maf.Lab.Eval/Suites/RetrievalSuite.cs`, compute recall@5, recall@20 and MRR over
      the answerable cases only, and a separate metric over the off-domain cases scoring how often
      nothing was retrieved. Verify with `make test-dotnet` that adding off-domain rows does not
      move the recall numbers.
- [x] 4.4 Report the new metric in the suite's progress line and make it gateable by
      `ctx.ThresholdsFor("retrieval")`, like the existing metrics. Verify by running
      `make eval SUITE=retrieval` and reading the report.

## 5. Choosing the number

- [x] 5.1 With the stack up and the corpus indexed, sweep the dense floor across the range the
      observed traces suggest (roughly 0.45 to 0.70) with the sparse floor null, running
      `make eval SUITE=retrieval` at each step. Record recall@5, recall@20, MRR and the off-domain
      metric per value in the change's notes.
- [x] 5.2 Sweep the sparse floor with the chosen dense floor fixed, over the BM25 range the traces
      show (roughly 1.0 to 4.0). Record the same metrics, and record whether any value behaves
      consistently across short and long queries.
- [x] 5.3 Pick the floors from the sweep: the values that leave recall@5 ≥ 0.6871, recall@20 ≥
      0.9116 and MRR ≥ 0.6347 — the accepted hybrid baseline in `evals/baseline.json` — while
      silencing the off-domain cases. If no such pair exists, leave the floors null, write down
      what the sweep showed and why nothing qualified, and stop here rather than choosing a number
      the data does not support.
- [x] 5.4 If floors were chosen, set them in the API and MCP configuration and accept a new
      baseline with `make eval SUITE=retrieval --accept-baseline`. Verify `make eval SUITE=all`
      passes afterwards.

## 6. The monitor shows the floor

- [x] 6.1 Extend the retrieval trace types in `web/src/monitor/traceData.ts` with the floors and
      the per-candidate dropped marker. Verify `make build-web` type-checks clean.
- [x] 6.2 Show the floors as chips beside the existing `limit · prefetch` chip in `RetrievalTab`
      in `web/src/monitor/MonitorTabs.tsx`. Verify by test that the chips render for a traced
      search.
- [x] 6.3 Mark dropped candidates in the ranked lists so they are visibly apart from returned
      ones, and say when a search returned nothing — distinguishing "all dropped" from "nothing
      found". Verify with tests in `web/src/monitor/MonitorPanel.test.tsx` covering both.
- [x] 6.4 Add fixtures in `web/src/monitor/fixtures.ts` for a search whose candidates were all
      dropped and for one that found nothing at all. Verify with `make test-web`.

## 7. Confirm end to end

- [x] 7.1 Run `make lint` and `make test` and verify both pass.
- [x] 7.2 With the stack running, ask the assistant a question the corpus cannot answer and verify
      the answer carries no sources, the Retrieval tab shows the dropped candidates and the floors,
      and the turn appears in `/admin/feedback` with the "zero retrieval results" signal.
- [x] 7.3 Ask a question the corpus can answer and verify it still retrieves, cites its sources,
      and raises no signal.
