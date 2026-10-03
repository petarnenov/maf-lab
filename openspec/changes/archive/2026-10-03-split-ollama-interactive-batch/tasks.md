# Tasks

## 1. Spike: threads under a cpuset

- [x] 1.1 Start a throwaway `ollama/ollama:0.34.2` container with `--cpuset-cpus 4-15` on the shared models volume, embed one text, and record the runner's `n_threads` from its log; verify by writing the result (follows the cpuset or stays 16) into design.md Decision 3, removing the Open Question

## 2. Compose

- [x] 2.1 Add the `ollama-batch` service (same image, `OLLAMA_KEEP_ALIVE`, `OLLAMA_NOPRUNE=1`, models volume, port 11436, healthcheck) and `cpuset` from `OLLAMA_INTERACTIVE_CPUS` (default `0-3`) on `ollama` and `OLLAMA_BATCH_CPUS` (default `4-15`) on `ollama-batch`; verify `docker compose config` is valid and `docker inspect` shows the two CPU sets
- [x] 2.2 Add `Models__BatchOllamaEndpoint: http://ollama-batch:11434` to `x-app-env`; verify `docker compose config` shows it on api, mcp-retrieval, mcp-portfolio, mcp-code
- [x] 2.3 Make `ollama-init` depend on both instances and only pull; add one-shot `ollama-warm` (`alpine:3.22`, busybox `wget --post-data` with each role's `num_thread`) and point the app services' `depends_on` at it; verify `docker exec` `ollama ps` on each instance shows `embeddinggemma` `UNTIL Forever` after `make`
- [x] 2.4 CI override: `ollama-batch` uses the stub image and healthcheck; verify `make ci-e2e` passes

## 3. Routing in code

- [x] 3.1 Add nullable `BatchOllamaEndpoint` to `ModelOptions` with a doc comment; verify the options binding test covers it
- [x] 3.2 In `ModelProviders`, keep an interactive and a batch embedder per vector (batch = interactive when the endpoint is unset); `DenseEncoder.EmbedQueryAsync` uses interactive, `EmbedDocumentsAsync` uses batch, keeping the `truncate=false` path; verify new unit tests: queries hit the interactive endpoint, documents the batch endpoint, and with no batch endpoint both hit the one endpoint
- [x] 3.3 Add `OllamaNumThread` / `BatchOllamaNumThread`, send `options.num_thread` per role on both the query and the document path (only when set), and set them in `x-app-env` and the host-side environment from `OLLAMA_INTERACTIVE_THREADS` (4) / `OLLAMA_BATCH_THREADS` (12); verify the unit test asserts the option per role and each runner log shows only one `threadpool init` with the expected `n_threads` after `make index` and a search

## 4. Host-side wiring

- [x] 4.1 `make infra` starts `qdrant neo4j ollama ollama-batch` then `ollama-init`; `HOST_ENV`, `scripts/index_if_empty.sh` and `scripts/dev.sh` export `Models__BatchOllamaEndpoint=http://localhost:11436` (overridable); verify `make down && make index` brings up both instances and the Ollama logs show `/api/embed` only on `ollama-batch` during the run
- [x] 4.2 `make doctor` warns when the Docker VM has fewer CPUs than the configured sets use; verify by running it with `OLLAMA_BATCH_CPUS=4-63`

## 5. Topology

- [x] 5.1 `TopologyProbe` reports `ollama-embeddings` with instances `interactive` and `batch`, probed concurrently, healthy/degraded/unreachable per the system-topology delta, and one instance when no batch endpoint is configured; verify topology unit tests for both up, batch down, and single-instance, and the draw.io match test still passes

## 6. Acceptance

- [x] 6.1 In a subagent, measure query-embed latency on the interactive instance idle and while `make index FORCE=1`-style batch embedding runs, plus batch throughput; verify p50 under load ≤ 2× idle p50, idle p50 ≤ 150 ms (else move the default split to 6/10 and re-measure), and record the numbers in DECISIONS.md
- [x] 6.2 Embed the same text as a document on both instances; verify cosine similarity ≥ 0.9999
- [x] 6.3 Run `make test`, `make lint` and `make verify`; verify all pass

## 7. Documentation

- [x] 7.1 README.md: architecture diagram and the published-ports paragraph name both instances and port 11436; the local-development section mentions `Models__BatchOllamaEndpoint`; verify by reading the rendered sections
- [x] 7.2 openspec/project.md: add `ollama-batch` to the containers list and say query and batch embeddings run on separate instances; CLAUDE.md: Embeddings line names interactive 11435 and batch 11436; check .github/copilot-instructions.md and update it if it lists the services or the port
- [x] 7.3 DECISIONS.md: entry for the split (isolation over replicas, measured numbers, CPU split, NOPRUNE, the 1.1 finding)
- [x] 7.4 Update the `infra` target's `##` comment, run `make docs` (no hand edits inside `generated:` blocks), then `make docs-check`; verify it passes
