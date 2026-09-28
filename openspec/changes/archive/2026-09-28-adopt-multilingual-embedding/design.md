# Design

## Context

See proposal.md — Why. What exists, and what constrains the switch:

- Embedding models are configuration: `ModelOptions.Embeddings` maps a Qdrant named vector to an `EmbeddingProfile`
  (model, dimensions, document/query prefixes). `Retrieval:DenseVector` picks the queried vector,
  `Indexing:DenseVector` the one written; `migrate --to <vector>` fills a provisioned vector restartably (§6).
- The collection has `dense_v1` (nomic-embed-text, 768) and `dense_v2` (all-minilm, 384). `CollectionBootstrapper`
  creates every configured vector on a new collection and **throws** when an existing collection lacks one, because
  Qdrant cannot add a named vector to an existing collection (verified on 1.19.1). Both finalists need a new vector:
  `embeddinggemma` is 768-d, `bge-m3` 1024-d, neither fits `dense_v2`.
- `Retrieval:DenseFloor` = 0.65 is one global number, swept for nomic against the retrieval eval.
- `QueryTranslator.NeedsTranslation` fires only on non-Latin letters, so Latin-script Bulgarian is never translated.
- The retrieval report already groups recall@5 by each case's `language` — a new value needs no code.
- CI's stub embeds by feature hashing at a dimension looked up by model name (`DIMENSIONS`), defaulting to 768.

The planning measurement (script kept in `probe/embed_bench.py`): every chunk embedded with each model's document
prefix, every answerable query with its query prefix, cosine top-5 over the chunks in the case's tenant scope.
It reproduces the live eval for the current model — dense EN 0.640 offline, 0.64 in the last retrieval report — so the
offline numbers are comparable to the eval's.

Score scales, top-1 cosine per query:

| model | answerable: min / p10 / median | off-domain: max |
|---|---|---|
| nomic-embed-text | 0.480 / 0.494 / 0.588 | 0.581 |
| embeddinggemma | 0.197 / 0.288 / 0.495 | 0.284 |
| bge-m3 | 0.422 / 0.515 / 0.669 | 0.517 |

## Goals / Non-Goals

**Goals:**

- Dense retrieval that works for English, Bulgarian and Latin-script Bulgarian without depending on translation.
- The choice made by the real hybrid eval under a rule stated before the numbers are in.
- Rollback by configuration, no re-index.

**Non-Goals:**

- Translating Latin-script Bulgarian for the BM25 half. Detecting a language written in someone else's alphabet is
  its own problem (and an LLM call per English query if done naively); the multilingual dense model is what covers
  this case here. Recorded as a follow-up.
- Changing chunking, BM25, fusion or rerank.
- Blue/green collections or aliases (below).

## Decisions

### Finalists and the rule that picks between them

`embeddinggemma` and `bge-m3` — the two that lead on at least one language. `nomic-embed-text-v2-moe` is dominated by
`embeddinggemma` on every language; `qwen3-embedding:0.6b` is the weakest on Bulgarian and the slowest (107 ms/query,
27 min to embed the corpus).

The rule, fixed now: run `make eval SUITE=retrieval` (hybrid, production settings, each finalist with its own
calibrated floor). **The higher worst-language hybrid recall@5 across `en`, `bg`, `bg-latn` wins.** If the two are
within 0.02 — the retrieval suite's measured run-to-run spread for Bulgarian — `embeddinggemma` wins: half the size
(622 MB vs 1.2 GB), half the query latency (44 vs 86 ms), 768-d vectors, and cleaner separation of off-domain queries
(its off-domain maximum 0.284 sits below the answerable p10 0.288; `bge-m3`'s overlap). A rule chosen after the
numbers would be a preference with a table attached.

*Rejected — ask the owner to pick:* the trade-off (English and speed vs Latin-script robustness) is exactly what the
eval measures; the owner asked for a multilingual model, and the worst-language rule is the literal form of that.

### Provision by rebuild

New profiles `dense_v3` (embeddinggemma, 768, prefixes `title: none | text: ` / `task: search result | query: `) and
`dense_v4` (bge-m3, 1024, no prefixes). A new indexer command, `rebuild`, with a `make rebuild-index` target:
prints the collection and point count it will discard, deletes the chunk collection (the BM25 statistics are
recomputed by indexing), lets the bootstrapper create it with every configured vector, and indexes the corpus with
`--force` — which, per the section below, writes every configured vector. It requires `--yes` (the
make target passes it when `FORCE=1`, as `make clean` does).

After the bake-off the loser's profile is removed and the index rebuilt once more, so the collection carries
`dense_v1` (rollback) and the winner. `dense_v2` (all-minilm) is dropped in that rebuild: it was a migration
exercise, never served, and its tests (`CollectionBootstrapTests`, `IndexingPipelineTests`, `FakeDenseEncoder`) move to
the winner's name.

*Rejected — blue/green collections behind a Qdrant alias:* zero downtime, but the existing `maf_chunks` is a
collection, not an alias, so adopting aliases is itself a migration of every client. The corpus is rebuildable from
`data/` in minutes; the lab does not need a swap.

*Rejected — make the bootstrapper recreate the collection when a vector is missing:* a start-up that silently deletes
the index is how a typo in configuration becomes an outage. Rebuild stays explicit.

### Every configured vector is written, and versioned per vector (found during implementation)

The first version of this design kept `dense_v1` "filled for rollback" — which the code could not keep. A point carries
one `model_version`, and indexing a changed document replaces its points with only `Indexing:DenseVector`'s vector,
so the first edit to a document silently drops its other vectors; a migration rewrites `model_version`, so the next
`index` run sees every document as stale and re-writes them all with one vector. The same weakness already sat under
§6's "rollback = switch back to dense_v1".

Chosen with the owner: indexing writes **every configured dense vector** for every chunk, and the payload records the
model per vector (`model_version__<vector>`, a flat keyword key so filters and partial payload updates need no nested
paths). `model_version` stays as the model of `Indexing:DenseVector`, which drift and status report. A document is
unchanged only when its content hash, `updated_at` and every per-vector model match. Migration fills one vector and
sets only that vector's key — and `model_version` only when the vector is the indexing one. Consequences:

- rollback is a configuration switch that stays true after any number of edits;
- `rebuild` is "delete the collection, bootstrap, index with `--force`", with no migrations chained behind it;
- the bake-off needs no special tooling: both finalists are filled by the same indexing run;
- indexing takes the sum of the configured models' time (measured for the full corpus: nomic 9 min, embeddinggemma
  9 min, bge-m3 15 min on this laptop); ordinary runs touch only changed documents.

*Rejected — rollback by rebuild:* smaller change, but it gives up "rollback without re-indexing" and leaves §6's
weakness in place.

### The floor belongs to the embedding

`EmbeddingProfile` gains `DenseFloor`; `Retrieval:DenseFloor` becomes an override (null = use the selected profile's).
`dense_v1` keeps 0.65. The finalists' floors are swept like the original (the method of the archived
`2026-09-22-add-retrieval-relevance-floor` change, whose result is the comment on `DenseFloor`): candidate values across the gap between off-domain maxima and answerable minima, keep the value that holds every
recall metric at or above what the model scores with no floor and silences the most off-domain questions. Starting
ranges from the table above: embeddinggemma 0.15–0.30, bge-m3 0.30–0.52.

### Translation unchanged

Cyrillic queries are still translated for both halves; with a multilingual dense model that costs nothing in dense
recall and keeps BM25 working. Latin-script Bulgarian still is not translated; the dense branch now handles it, and the
`bg-latn` column shows how well. Whether the dense half should see the *original* query instead is a measurable
follow-up, not part of this change.

### Dataset

24 `bg-latn` twins added to `evals/retrieval.jsonl`, generated by the same transliteration the planning probe used
(я→q, ж→j, ч→ch, ш→sh, щ→sht, ъ→a, ю→iu, ц→c — the common chat style, as in the query that prompted this), each
reviewed by hand and carrying the Cyrillic case's `relevantChunkIds`. Ids `r-xx-latn`.

## Risks / Trade-offs

- [The baseline moves on purpose] → Retrieval is re-accepted with `make eval-accept SUITE=retrieval` only after the
  bake-off, and the commit says which metrics moved and why. Every other suite must pass against its existing baseline.
- [Rebuild downtime] → Minutes, on demand, announced by the command; searches during it fail with the bootstrapper's
  error rather than returning partial results.
- [Model licence] → `embeddinggemma` ships under the Gemma Terms of Use, `bge-m3` under MIT. Both permit this use;
  recorded in DECISIONS.md next to the choice.
- [Larger model to pull on the evals runner] → +0.6–1.2 GB in `pull-models.sh`; the runner already pulls two models.
- [A 49-question dataset (73 with the twins) decides the model] → The rule's 0.02 band is the suite's measured noise;
  a win inside it goes to the cheaper model rather than to the table.

### One embedding in production (owner's decision, during implementation)

The design first kept `dense_v1` filled as a configuration-only rollback. The owner chose a single embedding: after the
bake-off the collection holds only the winner. Rollback becomes "re-add the previous profile with its floor, select
it, rebuild" — about ten minutes for nomic on this corpus — in exchange for indexing with one model instead of two.
Per-vector writing and versioning stay: they are what makes the bake-off (and any future comparison) safe, and they
cost nothing with one vector.

## Migration Plan

1. Merge with `Retrieval:DenseVector` still `dense_v1`; run `make rebuild-index FORCE=1` (provisions v1, v3, v4).
2. Bake-off, floors, pick the winner; remove the loser's profile; rebuild again.
3. Switch `Retrieval:DenseVector` and `Indexing:DenseVector` to the winner; re-accept the retrieval baseline.
4. Remove every profile but the winner and rebuild once more; the collection holds one dense vector.
5. Rollback: re-add the previous profile with its floor, select it, `make rebuild-index FORCE=1`.
