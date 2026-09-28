# Tasks

## 1. Measure what users type

- [x] 1.1 Add a `bg-latn` twin (`r-xx-latn`, `"language": "bg-latn"`, same `relevantChunkIds` and `firmId`) for each
      of the 24 Bulgarian answerable cases in `evals/retrieval.jsonl`, using the transliteration in
      `probe/embed_bench.py` and checking each by hand. Verify the file parses, ids are unique, and the current model's
      retrieval eval now reports `recall@5:bg-latn`.
- [x] 1.2 Record the current model's retrieval report with the twins as the comparison point (no baseline change yet).

## 2. The floor belongs to the embedding

- [x] 2.1 Add `DenseFloor` to `EmbeddingProfile` (dense_v1: 0.65) and make `Retrieval:DenseFloor` an override that
      defaults to null, meaning "the selected profile's". Resolve the floor in `DocumentSearchService` and in the eval's
      `RetrievalSuite.DefaultVariants` call from the selected profile. Verify unit tests: selecting a profile selects
      its floor; an explicit override wins; with dense_v1 the searched floor is still 0.65 and `make test` passes.

## 3. Provisioning by rebuild

- [x] 3.0 Write every configured dense vector at indexing time and version it per vector: `ChunkWrite` carries a
      vector per name; the payload gains `model_version__<vector>` (keyword-indexed) beside `model_version`; the
      pipeline embeds with each provisioned profile and treats a document as unchanged only when every per-vector model
      matches; migration filters and sets only its own vector's key (and `model_version` only for the indexing vector).
      Verify integration tests: an edited document keeps both vectors; a migration does not make the next index run
      re-write anything; existing pipeline and bootstrap tests pass.

- [x] 3.1 Add profiles `dense_v3` (embeddinggemma, 768, `title: none | text: ` / `task: search result | query: `) and
      `dense_v4` (bge-m3, 1024, no prefixes). Verify the bootstrapper against the existing collection fails naming
      both vectors and the rebuild command (update its message), and deletes nothing.
- [x] 3.2 Add the `rebuild` indexer command: print collection and point count, require `--yes`, delete the chunk
      collection, bootstrap, index with `--force` (every configured vector, per 3.0). Add
      `make rebuild-index` (passes `--yes` only with `FORCE=1`). Verify an integration test (Testcontainers Qdrant)
      rebuilds a small corpus and every point has every configured vector and model version.
- [x] 3.3 Pull the new models in `compose/pull-models.sh`; add their dimensions to the CI stub's `DIMENSIONS`. Verify
      `make up` pulls them and the model-free e2e (`COMPOSE_PROJECT_NAME=maf-lab-ci make ci-e2e`) still passes.
- [x] 3.5 Re-indexing a changed document deletes every point of its `doc_id` first, then upserts the new version
      (owner's decision; brings the code in line with the `document-indexing` spec, which always said "remove … before
      storing"). Verify an integration test: the delete count equals every old point, including one whose chunk id the
      new version reuses; DECISIONS §5 records the trade-off.
- [x] 3.4 Run `make rebuild-index FORCE=1` on the local stack. Verify `dense_v1`–`dense_v4` are filled for all 3330
      chunks and a search through the load balancer answers with `dense_v1` still selected.

## 4. Bake-off

- [x] 4.1 Sweep the dense floor for `dense_v3` (0.15–0.30) and `dense_v4` (0.30–0.52) against the retrieval eval with
      `Retrieval__DenseVector` set; keep for each the value that holds every recall metric at or above its no-floor run
      and silences the most off-domain questions. Record both sweeps.
- [x] 4.2 Run the retrieval eval three times per finalist at its floor. Apply the rule: higher worst-language hybrid
      recall@5 over `en`, `bg`, `bg-latn` wins; within 0.02, `embeddinggemma` wins. Write the table and the decision
      into DECISIONS.md.
- [x] 4.3 Keep only the winner (owner's decision): remove the `dense_v1`, `dense_v2` and loser profiles; set
      `Retrieval:DenseVector` and `Indexing:DenseVector` defaults to the winner; move tests, the fake encoder and the CI
      stub off the removed vectors; drop the removed models from `OLLAMA_PULL_MODELS`; rebuild again. Verify
      `make test` and `make lint` pass and the collection holds exactly one dense vector.

## 5. Nothing else moved

- [x] 5.1 Run `make eval SUITE=all`. Verify selection, generation, injection and confirmation pass against their
      existing baselines (re-run a noisy failure once, as before).
- [x] 5.2 Verify English recall@5 is at or above its accepted baseline, then `make eval-accept SUITE=retrieval` and
      check the diff of `evals/baseline.json` touches only the retrieval suite.
- [x] 5.3 With the stack running, ask "Kakva e procedurata kogato lipsva fee schedule?". Verify the retrieval
      diagnostics show the winner's vector, `translated: false`, and the missing-fee-schedule procedure among the
      sources.
- [x] 5.4 Document rollback (re-add the previous profile with its floor, select it, `make rebuild-index FORCE=1`) in
      DECISIONS.md, and verify the procedure is covered by the rebuild integration test with a profile added.
- [x] 5.5 Update DECISIONS.md (Models table, §6 provisioning by rebuild, §20 "the experiment" answered, licence) and
      the README's retrieval notes; `.github/workflows/evals.yml` and `CLAUDE.md` name the embedding models. Verify
      `make lint` and `make specs` pass.
