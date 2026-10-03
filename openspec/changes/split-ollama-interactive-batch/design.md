# Design

## Context

See proposal.md (Why) for the measurements. Current state that shapes the approach:

- One compose service `ollama` (`ollama/ollama:0.34.2`, CPU only), `OLLAMA_NUM_PARALLEL=1`, `OLLAMA_KEEP_ALIVE=-1`,
  models in the `ollama-models` volume (or the host's `~/.ollama` through `OLLAMA_MODELS_DIR`), host port 11435.
  `ollama-init` pulls `embeddinggemma` and warms it with `ollama run`.
- Every process reads one `Models:OllamaEndpoint`. In-stack services get `http://ollama:11434` from `x-app-env`;
  host-side CLIs get `http://localhost:11435` from `HOST_ENV` (Makefile), `scripts/index_if_empty.sh` and
  `scripts/dev.sh`.
- `ModelProviders.GetEmbedder(vectorName)` caches one `OllamaApiClient` per vector. `DenseEncoder.EmbedQueryAsync`
  goes through `IEmbeddingGenerator.GenerateAsync`; `EmbedDocumentsAsync` calls `OllamaApiClient.EmbedAsync` directly
  (for `truncate=false`). Every document path — `IndexingPipeline`, `MigrationService`, the api's
  `/admin/index/run`, eval's own indexing — goes through `EmbedDocumentsAsync`; every search goes through
  `EmbedQueryAsync`. That split is the one we need.
- The ollama image has no `curl`/`wget`; Ollama 0.34.2 has no environment variable for the thread count (only the
  per-request `options.num_thread`). The container reports 16 CPUs and the runner logs `n_threads = 16`.
- The topology probe reads `Models:OllamaEndpoint` for one `ollama-embeddings` node; the drawing has one node.
- CI swaps `ollama` for the stub image and makes `ollama-init` a no-op (`compose/docker-compose.ci.yml`).

## Goals / Non-Goals

**Goals:**
- A query embedding never waits behind a batch, neither in Ollama's queue nor for CPU.
- No change to vectors, the model, tenant handling, tools or endpoints.
- Unchanged behaviour wherever only one endpoint is configured (tests, local runs without compose).

**Non-Goals:**
- Faster batches. Batch throughput may drop a little, because the batch instance gets fewer CPUs than today.
  Quantization or another embedding runtime are separate decisions.
- Replicas or load balancing of either instance.
- Routing eval's search load away from the interactive instance (eval runs on demand; its queries are short).

## Decisions

### 1. Route by operation inside the dense encoder
`EmbedQueryAsync` uses the interactive client and `EmbedDocumentsAsync` the batch client. `ModelProviders` caches two
clients per vector: one on `Models:OllamaEndpoint`, one on `Models:BatchOllamaEndpoint` (new, nullable). When the batch
endpoint is null the batch client is the interactive one, which gives the single-instance fallback for free.

*Alternatives:* a per-process endpoint (the indexer simply points at the batch instance) — rejected: the api and eval
both search and index in one process, and the routing would silently depend on deployment wiring. A role parameter on
the encoder's methods — rejected: the operation already says which role it is.

### 2. A second compose service, not replicas
`ollama-batch`: same image, same `OLLAMA_KEEP_ALIVE`, host port 11436, the same models volume. `ollama` keeps its name
and port, so everything that reads 11435 or `http://ollama:11434` for search is untouched. A shared volume means one
pull; Ollama only reads blobs at load. `OLLAMA_NOPRUNE=1` on both, so neither prunes blobs at start while the other is
pulling.

*Alternative:* `deploy.replicas: 2` behind nginx — rejected by the measurements: on one CPU it adds no throughput, and
it does not separate queries from batches.

### 3. CPU isolation by `cpuset`, threads matched to the set
`cpuset` on each service from variables with defaults: interactive `OLLAMA_INTERACTIVE_CPUS=0-3`, batch
`OLLAMA_BATCH_CPUS=4-15` (Docker Desktop's VM shows 16 CPUs; the defaults assume that and are documented as such).
Hard pinning rather than `cpu_shares`: llama.cpp's threads synchronise at barriers, so oversubscribed threads stall
each other; non-overlapping sets remove contention completely.

The thread count must match the set, and Ollama does not derive it from the cpuset. Spike (2026-10-03, ollama
0.34.2, `--cpuset-cpus 4-15`): the runner still starts `n_threads = 16`. `options.num_thread: 12` on `/api/embed`
gives `n_threads = 12`, a repeat with the same value reuses the runner (65 ms), and a request *without* the option
reloads the runner back to 16 threads (a cold load). So:
- every embedding request sends `options.num_thread` for its role, from configuration (`Models:OllamaNumThread`,
  `Models:BatchOllamaNumThread`, sent only when set), on both the query and the document path;
- compose sets those keys from the same variables that size the CPU sets (`OLLAMA_INTERACTIVE_THREADS=4`,
  `OLLAMA_BATCH_THREADS=12`), in `x-app-env` and in the host-side environment, so all clients of one instance agree;
- nothing may load the model without the option: the `ollama run` warm-up goes (Decision 4).

*Alternative:* derived models via a Modelfile `PARAMETER num_thread` — rejected: a different model name per instance
would show up in `DenseModelVersions` and break "same model, same vectors" bookkeeping.

### 4. Warm both in `ollama-init`
`ollama-init` depends on both services being healthy and only pulls, through `ollama` (shared volume). `ollama run`
cannot pass `num_thread` (its stdin is embedded as text) and the ollama image has no HTTP client (no curl/wget; perl
without HTTP::Tiny). A new one-shot `ollama-warm` on `alpine:3.22` — already pinned for `api-data-init`, so no new
image — runs after `ollama-init` and POSTs one `/api/embed` with the role's `num_thread` to each instance with busybox
`wget --post-data` (verified in the spike). The application services depend on `ollama-warm` completing instead of
`ollama-init`.

### 5. Wiring
- `x-app-env`: `Models__BatchOllamaEndpoint: http://ollama-batch:11434`.
- `HOST_ENV`, `index_if_empty.sh`, `dev.sh`: `Models__BatchOllamaEndpoint=http://localhost:11436` (overridable).
- `make infra`: `up -d --wait qdrant neo4j ollama ollama-batch`, then `ollama-init`.
- CI override: `ollama-batch` uses the stub image and healthcheck like `ollama`. The stub's hash embeddings are
  deterministic, so both "instances" return the same vectors.

### 6. Topology: one node, two instances
`ollama-embeddings` probes both endpoints concurrently and reports instances `interactive` and `batch` (spec:
system-topology). With no batch endpoint configured it reports the one instance. The drawing keeps one node, so the
draw.io file and its match test are unchanged.

## Risks / Trade-offs

- [Batch runs on 12 CPUs instead of 16 → indexing ~25 % slower] → accepted for isolation; the split is a variable.
  The rebuild/index progress bars already show the rate.
- [Query latency on 4 CPUs is higher than on 16 when idle (44 ms measured on 16)] → the acceptance check measures it
  idle and under a running index; if idle p50 exceeds ~150 ms, move the default to 6/10.
- [Docker Desktop CPU numbering or a host with fewer CPUs makes the default sets invalid] → compose fails to start
  with a clear cpuset error; `make doctor` warns when the VM has fewer CPUs than the defaults use; the variables
  override.
- [Shared volume, two servers] → only `ollama-init` pulls, `OLLAMA_NOPRUNE=1` on both.
- [~1.2 GiB more RAM] → accepted; the VM has 31 GiB.
- [`num_thread` mismatch, or a request without it (a developer's manual `curl`, a future client), reloads the runner
  with 16 threads] → every client goes through the one encoder, which always sends the role's value; the warm-up sends
  it too; DECISIONS.md and the compose comment say so for manual use.

## Migration Plan

1. Merge; `make` recreates `ollama`, starts `ollama-batch`, `ollama-init` warms both. No re-index: same model, same
   vectors.
2. Rollback: unset `Models__BatchOllamaEndpoint` (single-instance fallback) or revert the commit; the extra service can
   simply be stopped.
