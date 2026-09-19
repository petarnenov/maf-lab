# Proposal

## Why

The corpus is English; the questions are not. Measured against the running stack, a Bulgarian question and its
English twin retrieve almost disjoint documents:

| Question | Documents shared with the English twin (top 5) |
|---|---|
| "каква е процедурата когато липсва фий схема" | **0 / 5** — returns code and a migration note, not the procedure |
| "как да затворя билинг период" | **0 / 5** — returns proration rules |
| "как се издава билинг кредит на клиент" | **0 / 5** — returns fee calculators |
| "какво означава кодът за грешка FS-REQUIRED" | 3 / 5 — only because `FS-REQUIRED` is Latin text BM25 can match |

Both halves of hybrid retrieval fail for the same reason: BM25 tokenises the question into Cyrillic terms that
appear nowhere in the index, contributing nothing, and the dense vector for a Bulgarian sentence lands far from
English chunks. The previous change made the agent *force* retrieval for such questions — so the assistant now
reliably grounds its answers in documents that are reliably wrong.

## What Changes

- A search query that is not in the corpus's language SHALL be translated to it before it is embedded and before
  BM25 encodes it. Retrieval then runs unchanged on the translated text.
- The answer keeps the user's language: this changes what is *searched for*, never what is said back.
- The original and the translated query are both reported in the retrieval diagnostics, so the monitor shows what
  was actually searched.
- Translation failure, a timeout or an unusable answer leaves the original query, which is today's behaviour.
- The retrieval eval gains the Bulgarian twins of its existing cases, so recall@5 for non-English questions is
  measured, reported next to the English figure, and cannot silently rot.

## Capabilities

### Modified Capabilities

- `hybrid-retrieval`: a new requirement for query-language normalisation — when it applies, what it guarantees,
  what happens when it fails, and that the tenant scope and the single query path are untouched.
- `eval-harness`: the retrieval dataset carries the language of each case and the report breaks recall down by
  language, so "works in English" can no longer hide "useless in Bulgarian".

## Impact

- `src/Maf.Lab.Retrieval/Search/` — a translator in front of the existing `RankAsync`, mirroring `LlmReranker`:
  configuration-switchable, its own model, graceful degradation, no content in logs.
- `src/Maf.Lab.Retrieval/Configuration/Options.cs` — corpus language, enable switch, model, timeout, cache size.
- `SearchDiagnostics` (+ `docs/trace-events.md`, the monitor's Retrieval tab) — the query actually used.
- `evals/retrieval.jsonl` + `src/Maf.Lab.Eval` — language on each case, per-language metrics in the report.
- `compose/ollama-stub` — answers a translation request, so `make ci-e2e` stays model-free.
- No change to tenancy, the single tenant-scoped query path, the tool schema or the HTTP contract.
