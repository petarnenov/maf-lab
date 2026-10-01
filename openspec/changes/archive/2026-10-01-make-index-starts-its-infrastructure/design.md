# Design

## Context

The indexer targets run `src/Maf.Lab.Indexing` on the host against the compose infrastructure (`HOST_ENV`: Qdrant
gRPC on `localhost:6334`, Ollama on `localhost:11435`). Only `make`/`make up` start those containers; `make dev`
starts them inline in `scripts/dev.sh` (`up -d qdrant ollama ollama-init`). The indexer's `Main` catches only
`OperationCanceledException`; any other exception escapes `async Main`, which the runtime reports as unhandled and
aborts with SIGABRT — hence the core dump. See proposal.md for the motivation.

## Goals / Non-Goals

**Goals:** every indexer target works from a stopped stack; a missing service is explained in one line; identical
behavior on macOS and Linux.

**Non-Goals:** starting the application services for indexing; health-checking the embedding model's quality;
changing `make dev` or `index-if-empty` (they run after infrastructure is already up).

## Decisions

- **A Make prerequisite, not a script.** `infra` is a two-line target (`$(COMPOSE) up -d --wait qdrant ollama`, then
  `$(COMPOSE) up --no-log-prefix ollama-init`). Make 3.81 handles a plain prerequisite; no shell logic needed, so
  nothing GNU- or bash-4-specific. `$(COMPOSE)` keeps `-p $(COMPOSE_PROJECT)`, so CI's `maf-lab-e2e` project and
  `CI_MODE=1`'s stub overlay are honored.
  *Alternative:* `index` calling `scripts/wait_healthy.sh` — that script waits on the whole stack (lb included),
  which is wrong for indexing.
- **`up --wait` for health, `ollama-init` in the foreground.** `--wait` blocks until the healthchecks in
  `docker-compose.yml` pass and fails naming the service otherwise; it is available in every compose v2 release on
  Docker Desktop and Linux. `ollama-init` is one-shot (`restart: "no"`); `--wait` treats an exited container as a
  failure in some compose versions, so it runs separately in the foreground, where its exit code is make's. Its
  script skips models already present, so the repeat cost is one `ollama show`.
  *Alternative:* `docker compose run --rm ollama-init` — creates a new container per run and leaves orphans on Ctrl-C.
- **Progress.** compose renders per-container state and elapsed time while waiting (an indeterminate bar per
  `progress-feedback`), plain lines when not a TTY; `ollama pull` renders its own download bar. No custom bar needed.
- **Classify connection failures in the CLI, by type only.** In `Program.Main`, a `catch` maps
  `RpcException { StatusCode: Unavailable }` → Qdrant (`Host:GrpcPort` from `QdrantOptions`) and an
  `HttpRequestException` whose inner exception is a `SocketException` → the embedding endpoint (`OllamaEndpoint`'s
  authority). The mapping lives in a small static helper so it can be unit tested. The exception message is never
  printed (it can carry URLs with credentials in other configurations); only service name and configured address.
  The bar already prints its failure line from `IndexWithProgressAsync` before rethrowing.

## Risks / Trade-offs

- [compose `--wait` on a very old Docker Desktop] → `make doctor` already requires compose v2; `--wait` exists since
  v2.1.1 (2021).
- [First `make index` on a machine without the model pulls ~600 MB] → expected; same as first `make`. `OLLAMA_MODELS_DIR`
  reuse of `~/.ollama` already applies.
- [`infra` adds ~1s to a repeat `make index`] → acceptable; the "about a second per corpus" scenario is unchanged.
