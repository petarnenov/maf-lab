# Tasks

## 1. Repeat run

- [x] 1.1 Run the indexer from its build output in every index target; rebuild only when its project closure's sources are newer; verify a second build check takes ~0.1 s
- [x] 1.2 Save the BM25 model only when `Rebuild` reports a change; verify with `Bm25Tests` (same texts → no change, version kept)
- [x] 1.3 Create only missing payload indexes; verify with `CollectionBootstrapTests` and `IndexingPipelineTests`

## 2. Embedding timeout

- [x] 2.1 Add `Models:EmbeddingTimeoutSeconds`, used by the Ollama embedding client; default it to 900 s in the indexer; verify with `EmbeddingTimeoutTests` and a live `make index-code`

## 3. Progress

- [x] 3.1 Add `ConsoleProgress` (terminal and plain modes, final outcome line); verify with `ConsoleProgressTests`
- [x] 3.2 Report stage and document counts from the pipeline and draw them in `index` and `rebuild`; verify with `IndexingPipelineTests` (first run and unchanged repeat reach done = total)

## 4. Verification

- [x] 4.1 Warnings-as-errors build of the solution and the tests above pass
- [x] 4.2 Live: a repeat `make index` over unchanged billing and portfolio corpora takes under 1.5 s each with nothing written; the code corpus shows its progress and re-embeds only changed files
- [x] 4.3 `make docs-check` succeeds
