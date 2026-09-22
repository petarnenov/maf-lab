# Proposal

## Why

The system already knows how to say "I found nothing." It says it in three places:

- `DocumentSearchService.SearchAsync` builds the hint *"No matching documentation. Rephrase as a
  question about a procedure, policy or term…"* when `top.Count == 0`.
- `ChatTurnRunner` sets `state.ZeroResults = true` when a `search_documents` call produced no
  sources, and `Summarise` labels that call *"no matching documentation"*.
- `TurnSignals.Compute` turns that into `TurnSignal.ZeroRetrievalResults`, which routes the turn
  into the `/admin/feedback` review queue — behaviour the web-ui spec already describes under
  "Feedback review queue", with a scenario for exactly this case.

None of it can ever run. A `grep` for `ScoreThreshold|MinScore|threshold` across
`Maf.Lab.Retrieval` returns nothing: no query in the system has a relevance floor. An approximate
nearest-neighbour search always returns its k nearest neighbours, however far away they are, so
`top.Count == 0` is unreachable for any query that is not an early-return. Three coordinated
pieces of machinery, a spec requirement and a review-queue scenario all describe a state the code
cannot reach.

What happens instead is worse than nothing happening. Sources are not chosen by the model —
`ChatTurnRunner.Summarise` makes a source out of **every row the tool returned**:

```csharp
case "search_documents" when s.TryGetProperty("results", out var results):
    foreach (var r in results.EnumerateArray())
        sources.Add(new SourceRef(Str(r, "docId"), …));
```

So a turn that honestly answers "we have no documentation on that" still carries five
authoritative-looking citations underneath it, and raises no signal at all.

Observed on conversation `c_3ed9519cbeb44f37a13281fe8493b8f5`: the question "Какво е JWE?" is
translated to "What is JWE?", tokenises to the single term `jwe`, which is outside the BM25
vocabulary — so the sparse branch is blind. The dense branch returns five unrelated chunks about
fee schedules, invoice dispatch and custodian reconciliation, at cosine scores of 0.46–0.47
against the 0.65–0.70 that on-domain questions score in the same corpus. The model answers
correctly that the documentation does not cover it. Five sources are attached. No signal fires.
The turn never reaches review.

## What Changes

- Each candidate branch of a search applies its own relevance floor. A dense candidate that is not
  close enough, and a sparse candidate that scores too low, are dropped before fusion ever sees
  them. When nothing clears the floor in either branch, the search returns no results, and the
  existing zero-result path runs for the first time: the refine hint, the `ZeroResults` flag, the
  `zero_retrieval_results` signal, the review queue.
- The floors are per branch, not on the fused result. Dense cosine and BM25 are not on one scale,
  and a reciprocal-rank-fusion score carries no notion of closeness at all — it is a function of
  rank and of how many candidates were fused, so a floor on it would be a number with no meaning.
- The floors are configuration, alongside the other retrieval knobs, and can be switched off.
- The monitor keeps showing what was found. Diagnostics report the floors that were applied and
  the candidates that fell below them, so an operator can see that the search did find things and
  that none of them was close enough. The floor filters what the model and the citations get, not
  what the operator gets.
- The retrieval eval gains off-domain cases — questions this corpus genuinely cannot answer — and
  a metric for how often the system correctly stays silent on them. Without those, the suite can
  only show that a floor does no harm; it cannot show that the floor does its job.

The floor value itself is not proposed here. It is calibrated against the eval suite as part of
the work, and the calibration is allowed to fail: if no value both preserves recall and silences
the off-domain cases, that finding is reported rather than papered over with a number that only
looks reasonable.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `hybrid-retrieval`: "Hybrid search with server-side fusion" gains the per-branch relevance
  floor and states that a search whose branches are all empty returns nothing.
- `retrieval-tool`: a new requirement for what the tool does when nothing clears the floor, and
  what its diagnostics must still report. Added rather than folded into "Retrieval diagnostics on
  request", which the unarchived change `fix-retrieval-tab-null-idf` already modifies.
- `eval-harness`: "JSONL datasets" gains the off-domain retrieval case, and "Metrics" gains the
  metric that scores silence on those cases.
- `web-ui`: a new requirement for the floor in the retrieval view. Added rather than folded into
  "Monitor views", which `fix-retrieval-tab-null-idf` already modifies.

## Impact

- `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs` — the floor on each `PrefetchQuery` and on
  the single-branch queries.
- `src/Maf.Lab.Retrieval/Search/DocumentSearchService.cs` — passing the floors, and keeping the
  diagnostic branch queries unfiltered.
- `src/Maf.Lab.Retrieval/Configuration/Options.cs` — the two new `RetrievalOptions` values.
- `src/Maf.Lab.Retrieval/Search/SearchDiagnostics.cs` — the floors and the dropped candidates.
- `evals/retrieval.jsonl`, `src/Maf.Lab.Eval/Datasets/`, `src/Maf.Lab.Eval/Suites/RetrievalSuite.cs`,
  `evals/baseline.json` — off-domain cases, the metric, the accepted baseline.
- `web/src/monitor/` — the floors as chips, dropped candidates marked.
- Tests in `tests/`, and the web monitor tests.
- No change to the MCP tool schema, no API change, no new dependency, no package version moves.
