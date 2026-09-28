# Proposal

## Why

A question the corpus cannot answer still gets documentation: the production hybrid search stays silent on only
**2 of 6** off-domain questions (`offDomainSilence` 0.33, DECISIONS §33), because BM25 always finds some rare term
and its raw score cannot say whether a match is close. The sparse floor was left unset for exactly that reason —
"until something other than a raw BM25 score can judge it" (`RetrievalOptions.SparseFloor`). The analysis of where
Jev can replace a model (2026-09-28) found such a judge: one Jev request per search, one yes/no question per fused
candidate — "does `passages[i]` address the subject of `query`?" — separated all 30 probe queries (off-domain
maximum 0.02–0.06, in-domain top 0.63–0.98) in about 320 ms. The same answers also order the candidates, which makes
Jev a reranker nine times faster than today's LLM listwise reranker (p50 320 ms vs 2830 ms), though weaker by MRR on
the probe sample (0.748 vs 0.903) and with scores that moved between runs (MRR 0.722 vs 0.625).

## What Changes

- **Relevance gate.** After fusion, a search asks Jev one request with one Noul per fused candidate (the top 20 at
  most). When no candidate reaches a configured relevance floor, the search returns no results — the existing empty
  result with its refine hint. A search where some candidate reaches it is returned exactly as fused: the gate decides
  *whether* the corpus answers, never *which* chunks. Switchable by configuration; whether it is on by default is
  decided by the retrieval eval against rules fixed before measuring (see design).
- **Jev reranker.** `Retrieval:Reranker` selects `llm` (today's listwise reranker, the default) or `jev`, which orders
  the candidates by the same Noul answers — the same request as the gate, never a second one. Rerank stays off by
  default (`Retrieval:RerankEnabled`); enabling Jev rerank by default is decided by the eval, after checking whether
  Jev's scores are stable run to run on identical input.
- **Bounded and failing open.** The Jev request has its own timeout. A timeout, rejection, malformed answer or missing
  key leaves the search exactly as it is today (no gate, fused order), with the reason in the diagnostics and a
  structured warning without content.
- **Shared Jev plumbing.** The Jev credential, auth handler, options and wire DTOs move from `Maf.Lab.Api` to
  `Maf.Lab.Retrieval` (which the api already references, as it does for the model factory), so the MCP retrieval
  server uses the same key handling as the intent classifier. No package is added.
- **Diagnostics and eval.** Traced searches report the relevance judgment (floor, per-candidate probability, outcome
  or failure reason, model, timing). The retrieval eval adds a variant with the gate flipped relative to production,
  and with `--rerank` compares the LLM and Jev rerankers side by side; Jev failures are counted apart from quality.
- **CI stays secret-free.** The Ollama/Jev stub answers the passage Nouls; `FakeJev` does the same for tests.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `hybrid-retrieval`: a new requirement for the relevance gate; "Optional rerank" gains a selectable Jev reranker
  that shares the gate's request and fails open within a bounded time.
- `retrieval-tool`: "A search that finds nothing close returns nothing" also covers a search the gate silenced;
  "Retrieval diagnostics on request" reports the relevance judgment.
- `eval-harness`: "Retrieval mode comparison" reports the gate and the two rerankers side by side, with Jev failures
  counted separately.
- `continuous-integration`: the model-free stub also answers passage relevance requests.

## Impact

- `src/Maf.Lab.Retrieval/Jev/` (new home of the Jev client plumbing), `Search/DocumentSearchService.cs`,
  `Search/SearchDiagnostics.cs`, `Rerank/` (relevance judge, Jev reranker), `Configuration/Options.cs`,
  `RetrievalServiceCollectionExtensions.cs`, `Program.cs`.
- `src/Maf.Lab.Api/Agent/Jev/` — the intent classifier uses the moved plumbing; behaviour unchanged.
- `src/Maf.Lab.Eval/` — retrieval variants and failure counting.
- `compose/ollama-stub/server.py`, `compose/docker-compose.yml` (comment: the retrieval server now uses the key it
  already receives), `tests/Maf.Lab.Tests/FakeJev.cs`, new and adjusted tests.
- `DECISIONS.md` — measurements and the on/off decisions; `evals/baseline.json` only if the gate ships on.
- Cost: one Jev request per search when the gate or Jev rerank is on (~2.8k input tokens, ≈ $0.00012), and its
  latency on every search.
