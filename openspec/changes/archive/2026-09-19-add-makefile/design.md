# Design

## Context

See proposal.md. The commands the Makefile drives already exist: compose (`compose/docker-compose.yml`, lb on 7171,
api/mcp ×2), the host-side indexing CLI (`src/Maf.Lab.Indexing`, which needs Qdrant on 6334 and embeddings from the
compose Ollama on 11435), the eval CLI, `dotnet test --solution maf-lab.sln`, `npm test -- --run` / `npm run lint`
in `web/`, and `scripts/verify_lb.sh`. The dev machine runs macOS with **GNU Make 3.81** (`/usr/bin/make`), .NET in
`~/.dotnet` (not always on a non-login PATH) and Node through nvm.

## Goals / Non-Goals

**Goals:**
- `make` = working lab at http://localhost:7171; every documented workflow is a target; failures stop with a clear message.
- Compatible with GNU Make 3.81 and with GNU Make 4.x.

**Non-Goals:**
- Replacing compose, the CLIs or npm scripts; make only orchestrates them.
- Windows support (WSL works as Linux).

## Decisions

### D1. Default goal `all` = doctor-lite → up → wait → index-if-empty → banner
`.DEFAULT_GOAL := all`. `up` runs `docker compose up -d --build` (Docker's build cache makes unchanged images a no-op)
with `--scale api=$(API_REPLICAS) --scale mcp-retrieval=$(MCP_REPLICAS)`. `scripts/wait_healthy.sh` polls
`docker compose ps --format json` until every running service is healthy (timeout `WAIT_TIMEOUT`, default 300 s);
on timeout it prints the unhealthy services and the last 30 log lines of each and exits 1.
`scripts/index_if_empty.sh` reads the point count of the `maf_chunks` collection from Qdrant's REST API and runs the
indexing CLI only when the collection is missing or empty.

### D2. Make 3.81 compatibility
No `.ONESHELL`, `.SHELLFLAGS`, `$(file …)`, `guile` or grouped targets (all newer than 3.81). Multi-line logic lives
in `scripts/*.sh` with `set -euo pipefail`; recipes stay one command per line with `SHELL := /bin/bash`. Help text comes from
`## ` comments after each target, extracted with awk (works on 3.81).

### D3. Tool discovery
`DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo $(HOME)/.dotnet/dotnet)`, with `DOTNET_ROOT` exported when it
falls back to `~/.dotnet`. `NPM ?= npm`. The `require-%` pattern targets (`require-docker`, `require-dotnet`,
`require-npm`) check a tool and fail with a named message; they are prerequisites of the targets that need them.

### D4. Variables
`CHAT_MODEL` (default `gpt-oss:120b`), `API_REPLICAS`/`MCP_REPLICAS` (2), `SUITE` (all), `OLLAMA_MODELS_DIR`
(default `$HOME/.ollama` when that directory exists, otherwise the compose named volume), `BASE_URL`
(http://localhost:7171), `WAIT_TIMEOUT` (300), `FORCE`. They are exported to the compose process. `OLLAMA_API_KEY` is
only ever passed through the environment. `doctor` prints "set"/"missing", never the value.

### D5. Data targets use the compose infrastructure
`index`/`reindex`/`drift`/`migrate` run the indexing CLI on the host with
`Models__OllamaEndpoint=http://localhost:11435` so embeddings come from the compose Ollama, and Qdrant from the
published 6334. `reindex` passes `--force`. `migrate` uses `TO=dense_v2` by default.

### D6. Quality targets
`test` = `test-dotnet` + `test-web`. `lint` = `dotnet build -warnaserror` + web lint. `verify` = `scripts/verify_lb.sh
$(BASE_URL)`. `eval` = eval CLI with `--suite $(SUITE)` against the stack's MCP (`Evals__McpEndpoint=$(BASE_URL)/mcp`);
`eval-<suite>` is a pattern target.

### D7. `dev` (no Docker for app services)
It starts Qdrant and Ollama through compose, stops the compose app services and the lb, then runs mcp-retrieval, api and
vite in the foreground with prefixed output and a trap that stops all three on Ctrl-C. It lives in `scripts/dev.sh`.

### D8. Cleanup
`down` = `compose down` (volumes kept). `clean` asks for confirmation (skipped with `FORCE=1`), then runs
`compose down -v` and removes `bin/`, `obj/`, `web/dist` and `evals/reports`. Datasets are never deleted.

### D9. Balancer reload (found during implementation)
`make verify` exposed a race in the load balancer's dynamic DNS re-resolution, which came from the previous change:
a refresh during failover could leave a request with no retry peer. The upstreams are now resolved at nginx (re)load,
with `max_fails=1`, and `make up` reloads the balancer after every start or scale. `verify_lb.sh` checks were also made
deterministic: at least 8 job-status polls, and the dedup check accepts a new job id only when the first job had
already finished.

## Risks / Trade-offs

- [Make 3.81 quirks] → the logic lives in shell scripts, and the Makefile is tested with `make -n`, `make help` and a real `make`.
- [Host tools missing for `index`] → `require-dotnet` fails early. The index could later move into a one-shot
  container if host .NET becomes a burden (follow-up, out of scope).
- [`OLLAMA_MODELS_DIR` defaulting to `~/.ollama` shares models with a host Ollama] → read-mostly sharing has worked in
  this lab. Override with an empty value to use the compose volume.
