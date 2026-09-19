# Design

## Context

See proposal.md for the measured gap. What it has to fit into:

- `DocumentSearchService.RankAsync` is the one place a query becomes vectors: `dense.EmbedQueryAsync(...)` and
  `Bm25Encoder.EncodeQuery(model, query)`, then `search.QueryAsync` — the single tenant-scoped query path, which
  an IL-scanning test keeps single.
- `LlmReranker` is the precedent for a model call inside the retrieval server: `IChatClientFactory`, its own
  configured model, any failure degrades to the previous order, nothing of the content is logged.
- `SearchDiagnostics` already carries the query and its BM25 terms to the monitor's Retrieval tab through the tool
  result `_meta`, which the model never sees.
- The eval harness calls the MCP tool directly, so anything that lives in the agent would not be measured by it.

## Goals / Non-Goals

**Goals:**
- A Bulgarian question retrieves what its English twin retrieves.
- The fix is measured, per language, by the retrieval eval — before and after.
- A question in the corpus language costs nothing extra.

**Non-Goals:**
- Changing the answer's language, or the system prompt.
- Translating documents, or indexing a second language.
- Detecting the language as a user-visible feature.
- Making BM25 itself multilingual (stemming, Cyrillic analysers) — the index is English either way.

## Decisions

### Normalise in the retrieval server, not in the agent
The translation happens in `DocumentSearchService`, immediately before the query is embedded and encoded. That is
the only point every caller passes through: the agent's forced call, a model-chosen call, the eval harness and a
raw MCP client all get the same behaviour, and the diagnostics that already flow to the monitor can carry it.

Alternative rejected — **tell the model to search in English** (a system-prompt line): free, but it only helps the
agent path, leaves the eval harness measuring something else, and makes retrieval quality depend on a prompt the
model may ignore. It is also invisible: nothing would show why a search did or did not work.

### Translate, rather than switch to a multilingual embedding model
A multilingual dense model (bge-m3, multilingual-e5) would fix the dense half without a model call per query, and
`dense_v2` is already provisioned for exactly this kind of experiment. It is rejected as the primary fix because it
leaves the sparse half broken — BM25 would still tokenise Cyrillic terms that appear in no chunk — so hybrid search
would degrade to dense-only precisely for the questions that need help most. Translation fixes both halves at once.

The experiment is not closed off: the eval now reports recall per language, so pointing `dense_v2` at a
multilingual model and re-running `make eval-retrieval` becomes a measurement rather than an argument.

### The translator mirrors the reranker
`IQueryTranslator` with a no-op implementation and an LLM implementation, chosen by configuration
(`Retrieval:NormalizeQueryLanguage`, default on; `Retrieval:CorpusLanguage`, default `en`;
`Models:TranslationModel`, default the chat model; a timeout like the reranker's). The prompt asks for the question
in the corpus language and nothing else, and states that identifiers, codes and file names are to be copied
verbatim. The answer is used only when it is non-empty and plausible (not longer than a small multiple of the
original); anything else leaves the original query. Every failure path is a log line without the query text.

### Only a query that needs it is translated
A query whose letters are all Latin is treated as already in an English corpus's language and is not sent
anywhere — that keeps every existing English path (evals, CI, the whole current corpus of questions) at exactly
today's cost and behaviour. The check is a property of the script, not a language model: cheap, deterministic, and
wrong only in ways that cost one unnecessary translation (for example a Latin-script German question), never a
missed one for Cyrillic. It is a configuration-level assumption that the corpus language is Latin-script; a
non-Latin corpus would need the check inverted, which the configuration makes explicit rather than hidden.

Translations are cached in memory per retrieval replica (small LRU, keyed by the query), so a repeated question —
and the eval harness running the same case in three modes — pays once.

### What the monitor and the eval see
`SearchDiagnostics` gains `query.original`, `query.searched` and `query.translationMs` (null when nothing was
translated), so the Retrieval tab shows the text that produced the terms it lists. The eval report gains
`recallByLanguage`, and `evals/retrieval.jsonl` gains an optional `language` on a case; rows without one count as
the corpus language, so the existing dataset is unchanged.

## Risks / Trade-offs

- **A wrong translation retrieves the wrong documents** → the diagnostics show exactly what was searched, and the
  eval measures it per language instead of trusting it.
- **Latency on the first non-English search** (one small model call, ~0.5–1 s measured for the intent classifier on
  the same endpoint) → bounded by a timeout, cached afterwards, and never paid by an English query.
- **The script check misjudges a Latin-script non-English query** → it searches as written, which is exactly
  today's behaviour, and the case is visible in the diagnostics.
- **The corpus language is configuration, not detection** → stated in the spec and in `DECISIONS.md`; a mixed-language
  corpus is out of scope and would need per-document language at index time.

## Migration Plan

Additive and stateless: no schema change, no re-indexing, nothing stored. Switching it off
(`Retrieval:NormalizeQueryLanguage=false`) restores today's behaviour exactly, which is also the rollback.

## Open Questions

- Whether the translated query should be searched *in addition to* the original (both queries fused) rather than
  instead of it — decidable from the eval numbers once the per-language figures exist, and it does not change the
  spec, which fixes only that the searched text is normalised.
