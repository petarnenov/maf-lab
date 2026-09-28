# Design

## Context

`DocumentSearchService.RankAsync` is the one path every search takes: translate → embed → BM25 encode → one
tenant-scoped Qdrant query (`TenantScopedSearch`, floors per branch, RRF) → optional rerank. The retrieval eval calls
`RankAsync` directly (k = 20); `search_documents` calls it through `SearchAsync` (k = 20 as well: `max(limit×2, 20)`).
Off-domain silence is 0.33 on the accepted baseline (hybrid); the dense floor silences two of six off-domain questions
and the sparse branch has no floor, because no BM25 value buys the silence without costing Bulgarian recall.

Jev today is used only by the api (`JevIntentClassifier`): `JevCredential` reads `JEV_MAF_LAB`, `JevAuthHandler` on the
named `"jev"` client is the only reader, DTOs in `JevContracts.cs`, options in `JevOptions` (section `Jev`). The
compose file already passes `JEV_MAF_LAB` to every app service through the shared `x-app-env` anchor, including
`mcp-retrieval`; nothing there reads it yet.

Measured before designing (planning probes, `jev-1.13.0`, 2026-09-28):

- Relevance on dense top-20 shortlists (analysis report §2): off-domain maximum 0.02–0.06, in-domain top 0.63–0.98.
- **Determinism** (this change's probe, the identical request sent five times back to back, five queries): Jev is not
  deterministic. Per-passage probabilities move by 0.01–0.05 typically and up to 0.19; every one of the five repeats
  produced a different order of the 20 passages, so the MRR spread seen in the analysis (0.722 vs 0.625) is the model,
  not ties. Ties from two-decimal rounding exist as well (up to 5 of 20 values in one answer) but are secondary. The
  **maximum** is stable: ±0.01–0.03 on every query (off-domain 0.06–0.07; in-domain 0.87–0.96). A gate reads the
  maximum; a reranker reads the whole order.

## Goals / Non-Goals

**Goals:** silence searches the corpus cannot answer without losing recall on the ones it can; offer Jev as a cheap
reranker; one Jev request per search at most; every Jev failure fails open, bounded in time, visible in diagnostics.

**Non-Goals:** replacing the branch floors; dropping individual chunks by relevance (recall@20 counts secondary chunks
a "does it address the query" judge scores low — the gate is per query, not per chunk); changing `IReranker`'s
tenant contract; pairwise rerank (20 requests per search would exhaust the rate limit at ~60 searches/min).

## Decisions

### D1. A query-level gate after fusion, before rerank

`RankAsync` asks Jev about the first *n* fused candidates (`Retrieval:RelevanceCandidates`, 20). If the judgment
succeeds and its maximum is below `Retrieval:RelevanceFloor`, the search returns **no candidates**; otherwise the
fused list is returned untouched. Why the maximum and not per-chunk filtering: the gate must answer "does the corpus
answer this at all?" — the question the floors cannot answer for BM25 — and the maximum is the stable part of Jev's
answer (above). Per-chunk filtering would change recall@5/@20 for answerable queries through the noisy part.
Only candidates `TenantScopedSearch` already returned are sent; the gate builds no query and holds no tenant.

### D2. The question and the state

State: `{ "query": <searched text>, "passages": [{ "section": <section path>, "text": <first 400 chars> }, …] }`;
questions `p0…p{n-1}`, each a Noul: "Does `passages[i]` address the subject of `query`?" — the probe's wording, kept
verbatim because its numbers are the evidence. The query is the text actually searched (after translation), the same
text the reranker already sees. Query and passages are data in the state, never in the instructions. Passage text is
corpus content: a passage crafted to steer Jev could at most raise or lower its own probability — the gate cannot
widen the tenant scope and the model still sees only the fused, tenant-scoped snippets.

### D3. The Jev reranker shares the gate's request

`Retrieval:Reranker` = `llm` (default, today's `LlmReranker`) | `jev`. `IReranker` gains `Kind`; `JevReranker` orders
by the judgment's probabilities, highest first, ties by fused rank (so equal answers keep the fusion's order), and
unjudged candidates keep their fused order after the judged ones. `DocumentSearchService` obtains the judgment once
per search when the gate is on **or** the selected reranker is Jev, and hands the same judgment to both. The score a
Jev-reranked snippet carries is the probability.

### D4. Bounded, failing open, visible

`Retrieval:RelevanceTimeoutSeconds` (default 2, the intent classifier's budget; the probe's p50 was 320–525 ms) with the
same `Task.WhenAny` race the classifier uses, so a transport that ignores cancellation delays the search by the budget
and no longer. Timeout, non-success status, missing or non-numeric answers, missing key → the judgment carries a reason
and no scores; the gate does nothing and the reranker returns the fused order. A structured warning names the reason
and the candidate count, never the query or passages. Diagnostics (when traced) carry `relevance`: floor, candidates
judged, maximum, outcome (`answered` / `silenced`) or failure reason, model, duration and the per-chunk probabilities.
A timing is recorded under the existing `retrieval.stage` histogram as stage `relevance`.

### D5. Where the Jev plumbing lives

`JevCredential`, `JevAuthHandler`, `JevOptions` and the wire DTOs move to `src/Maf.Lab.Retrieval/Jev/`
(`Maf.Lab.Retrieval.Jev`), with an `AddJevClient` registration that is idempotent (the api registers both the
retrieval core and the classifier). `Maf.Lab.Api` already references `Maf.Lab.Retrieval` for `IChatClientFactory`;
`Maf.Lab.Domain` stays contracts only and `Maf.Lab.Hosting` stays free of service-specific clients. The credential is
constructed from the configuration passed to the registration, so hosts that do not register `IConfiguration` in DI
(the eval's contextual variant, the indexer) still resolve it. The classifier and the DTO shapes are unchanged; the
state becomes an object so each caller sends its own named fields. The retrieval server resolves the credential at
startup so a missing key is warned about once, as in the api.

### D6. Configuration and eval

`RetrievalOptions`: `RelevanceGateEnabled`, `RelevanceFloor` (starting at the probe's 0.3; tuned on the real eval before
the on/off decision), `RelevanceCandidates` (20), `RelevanceTimeoutSeconds` (2), `RelevancePassageChars` (400),
`Reranker` (`llm`). `SearchSettings` gains `RelevanceGate` and `Reranker` so the eval compares variants in one process:
the primary `hybrid` variant runs production settings; a `hybrid+gate` or `hybrid-nogate` variant flips the gate; with
`--rerank`, `hybrid+rerank-llm` and `hybrid+rerank-jev` run side by side. The suite collects each search's relevance
outcome and prints Jev failures (timeouts, rejections) separately from quality, plus the off-domain maximum and the
lowest in-domain maximum, so one run shows the margin around the floor.

### D7. Decision rules, fixed before measuring

- **Gate on by default** only if, over ≥ 3 runs of the real retrieval eval (hybrid, production settings), off-domain
  silence rises and recall@5 for en, bg and bg-latn each stays within 0.02 of the accepted baseline
  (0.7133 / 0.7778 / 0.6181). Otherwise it ships off, configurable, with the numbers recorded.
- **Jev rerank on by default** only if hybrid recall@5 and MRR with Jev rerank beat no-rerank beyond the suite's
  run-to-run spread over ≥ 3 runs. Otherwise it stays selectable and off.

## Risks / Trade-offs

- [Jev degraded: 22–36 s responses and 503s observed] → the 2 s budget and fail-open; eval timeouts are reported apart
  from quality so an outage is not read as a quality result.
- [An in-domain question whose best candidate Jev scores under the floor loses all results] → measured by recall@5 per
  language under the decision rule; the floor is tuned between the lowest in-domain and highest off-domain maximum.
- [Latency: every search pays one Jev request (~300–500 ms) when the gate is on] → recorded; the LLM reranker it can
  replace costs ~2.8 s.
- [Rerank noise: Jev's order is not reproducible] → the rule demands a win beyond run-to-run spread; ties broken by
  fused rank keep equal answers stable, the rest is the model.
- [Passage injection] → can move only that passage's own probability; scope and content the model sees are unchanged.

## Migration Plan

Configuration only. With `JEV_MAF_LAB` unset the retrieval server behaves exactly as before (reason `no key` in
diagnostics). Rollback: `Retrieval__RelevanceGateEnabled=false`, `Retrieval__Reranker=llm`.

## Open Questions

None blocking. Whether a mean of two Jev answers per passage would make the reranker reproducible enough is left for a
follow-up if the single-answer reranker is close.
