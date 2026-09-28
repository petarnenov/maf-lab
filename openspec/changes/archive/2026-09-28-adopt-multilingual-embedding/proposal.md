# Proposal

## Why

The dense half of retrieval runs on `nomic-embed-text`, an English model. Bulgarian works today only because
`QueryTranslator` translates a query into English first — and it translates only text that contains non-Latin letters.
Bulgarian written in Latin letters ("Procedurata kak...", "Kakva e procedurata kogato lipsva fee schedule?") is
treated as English, is never translated, and reaches an English embedding model as noise. The code says so itself:
"its only failure mode (a Latin-script question in another language) costs a missed translation". The trace of the
turn that prompted this showed exactly that: `translated: false`, every BM25 term out of vocabulary.

Measured during planning (dense only, no translation, tenant-scoped like search, over the answerable cases of
`evals/retrieval.jsonl` plus a Latin-script twin of every Bulgarian case):

| model | dims | recall@5 EN | BG | BG in Latin letters |
|---|---|---|---|---|
| `nomic-embed-text` (current) | 768 | 0.640 | 0.188 | 0.208 |
| `embeddinggemma` | 768 | **0.793** | **0.722** | 0.597 |
| `bge-m3` | 1024 | 0.700 | **0.722** | **0.681** |
| `nomic-embed-text-v2-moe` | 768 | 0.733 | 0.653 | 0.535 |
| `qwen3-embedding:0.6b` | 1024 | 0.733 | 0.604 | 0.417 |

A multilingual model roughly triples recall for Bulgarian the translator misses and, on this corpus, is better in
English too. DECISIONS §20 left exactly this open: "pointing `dense_v2` at a multilingual model is a measurement
rather than an argument". This is that measurement, and the owner has asked for a multilingual embedding model.

## What Changes

- The production dense embedding becomes a multilingual model, chosen between the two finalists (`embeddinggemma`,
  `bge-m3`) by the real hybrid retrieval eval under a rule fixed now: the higher **worst-language** recall@5 across
  English, Bulgarian and Latin-script Bulgarian wins; within 0.02 of each other, the smaller, faster model
  (`embeddinggemma`) wins.
- A new named vector is provisioned for it. Qdrant cannot add a named vector to an existing collection, so the chunk
  collection is rebuilt from the corpus by a new, explicit rebuild command. When the bake-off is decided, the collection
  keeps **one** dense embedding — the winner. `nomic-embed-text`, `all-minilm` and the losing finalist are removed
  (owner's decision); rollback is re-adding the previous profile and rebuilding (~10 minutes).
- The dense relevance floor moves from one global number to the embedding it was calibrated for, and is recalibrated
  for the new model. Planning showed why it must: the current 0.65 is above the median score of *answerable* queries
  under `embeddinggemma` (0.495) — kept global, it would silence every search.
- Query translation stays as it is (Cyrillic → English for both halves). The multilingual model covers what
  translation misses; translation still serves the BM25 half, which a dense model cannot help.
- The retrieval dataset gains a Latin-script twin of every Bulgarian case, so the per-language report measures the
  script users actually type.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `hybrid-retrieval`: the dense branch uses a multilingual embedding; the dense floor belongs to the embedding it was
  calibrated for.
- `document-indexing`: provisioning a new dense vector by rebuilding the collection from the corpus, keeping the
  collection with exactly the configured embeddings.
- `eval-harness`: the retrieval dataset covers every script the questions arrive in, and recall is reported for each.

## Impact

- `src/Maf.Lab.Retrieval/Configuration/Options.cs` — new embedding profile(s), floor per profile, `DenseVector`
  default.
- `src/Maf.Lab.Retrieval/Search/` — the dense floor read from the selected profile.
- `src/Maf.Lab.Indexing/` — a rebuild command; `Makefile` target.
- `compose/docker-compose.yml`, `compose/pull-models.sh`, `compose/ollama-stub/server.py` (dimensions),
  `.github/workflows/evals.yml` (model pulled on the runner).
- `evals/retrieval.jsonl` (+24 Latin-script cases), `evals/baseline.json` (retrieval re-accepted, intentionally).
- `DECISIONS.md` — Models table, §6 and §20 updated, the bake-off numbers.
- Operational: one rebuild of the index (~10–15 min on a laptop for the chosen model); +0.6–1.2 GB model on disk.
