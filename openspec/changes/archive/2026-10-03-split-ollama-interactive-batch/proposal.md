# Proposal

## Why

One CPU-only Ollama serves both the live search's query embeddings and every long-running batch (indexing, migration,
rebuild, `/admin/index/run`). It runs one request at a time (`OLLAMA_NUM_PARALLEL=1`) on all 16 threads, so a
search query that arrives behind a 32-chunk batch waits for it: a batch of long chunks takes ~20 s, while a warm
query embed takes ~44 ms. Measured on 2026-10-03: throughput stays at ~1.7 chunks/s at any batch size or client
count, so more replicas of the same instance cannot help. The fix is isolation, not scale: search gets an Ollama of
its own that a batch can never queue in front of.

## What Changes

- Two Ollama services in compose, the same image and the same model (`embeddinggemma`, vector `dense_v3`):
  - `ollama` — **interactive**: query embeddings only (search in mcp-retrieval, mcp-portfolio, mcp-code, eval
    queries). Keeps host port 11435.
  - `ollama-batch` — **batch**: document embeddings (indexing, `make index*`, `reindex`, `rebuild-index`, `migrate`,
    `/admin/index/run`, eval's own indexing). Host port 11436.
- Each instance gets a fixed share of the CPUs (a small one for interactive, the rest for batch), so a batch at full
  load cannot slow a query down either through the queue or through CPU contention.
- Routing is by operation, not by caller: the dense encoder sends `EmbedQueryAsync` to the interactive endpoint and
  `EmbedDocumentsAsync` to the batch endpoint. A new `Models:BatchOllamaEndpoint` names the batch endpoint; when it is
  unset, documents go to `Models:OllamaEndpoint` as today, so local runs, tests and a single-Ollama setup keep working.
- `ollama-init` pulls the model once (shared models volume); a new one-shot `ollama-warm` loads it in both instances
  with each one's thread count, and both keep it loaded (`OLLAMA_KEEP_ALIVE=-1`).
- `make infra` starts both instances; host-side CLIs get the batch endpoint (`localhost:11436`) as well as the
  interactive one.
- CI mode replaces both with the Ollama stub.
- The topology report shows the embedding provider with two instances, `interactive` and `batch`, each with its own
  health.

No tool, endpoint or query builder gains a parameter; tenant handling is untouched. No model changes: both instances
serve the same model and produce the same vectors, so nothing is re-indexed.

## Capabilities

### New Capabilities
- `embedding-isolation`: query embeddings and batch embeddings are served by separate Ollama instances with separate
  CPU shares, so a batch never delays a search; routing by operation and the single-endpoint fallback.

### Modified Capabilities
- `make-workflow`: "Index targets start their infrastructure" — `make infra` starts both Ollama instances and the
  model pull/warm-up for both.
- `system-topology`: adds "Embedding provider instances by role" — the embeddings entry lists its `interactive` and
  `batch` instances, each with its own health.

## Progress feedback

No new CLI tool, make target or UI action. `make infra` keeps its existing wait-with-progress; it now waits for one
more service.

## Impact

- `compose/docker-compose.yml` (new `ollama-batch` service, CPU sets, `x-app-env` gains the batch endpoint),
  `compose/docker-compose.ci.yml` (stub for `ollama-batch`), `compose/pull-models.sh` (warm both).
- `src/Maf.Lab.Retrieval/Configuration/Options.cs` (`BatchOllamaEndpoint`, per-endpoint thread count if needed),
  `src/Maf.Lab.Retrieval/Models/ModelProviders.cs` (two embedder clients per vector: interactive and batch).
- `src/Maf.Lab.Api/Topology/TopologyProbe.cs` (two instances under `ollama-embeddings`).
- `Makefile` (`infra`, `HOST_ENV`), `scripts/index_if_empty.sh`, `scripts/dev.sh`.
- Unit tests for routing and the fallback; topology tests.
- RAM: one more loaded model (~1.2 GiB).

## Documentation impact

- README.md: the architecture diagram and the "only Qdrant and Ollama are published" port paragraph name both
  instances and port 11436; the local-development section mentions the batch endpoint. The `make infra` row in the
  `generated:make-targets` block changes only through the Makefile's `##` comment and `make docs`.
- openspec/project.md: the containers list gains `ollama-batch` and `ollama-warm`; the Models bullet says query and batch embeddings run
  on separate instances. The generated project-context in `openspec/config.yaml` follows through `make docs`.
- DECISIONS.md: a decision entry for the split (why isolation, not replicas; measured numbers; CPU split). Updated
  further if a new image is needed for the warm-up (see design).
- CLAUDE.md: the Embeddings line notes the two instances (interactive 11435, batch 11436).
- .github/copilot-instructions.md: checked; updated only if it lists the compose services or the Ollama port.
