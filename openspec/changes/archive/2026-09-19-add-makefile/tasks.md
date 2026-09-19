# Tasks

## 1. Scripts

- [x] 1.1 Add `scripts/wait_healthy.sh` (poll compose until all running services are healthy; on timeout print unhealthy services + last 30 log lines, exit 1); verify it returns 0 on the running stack and 1 with a 1-second timeout during startup
- [x] 1.2 Add `scripts/index_if_empty.sh` (Qdrant point count via REST; run the indexing CLI only if the collection is missing or empty); verify it skips on the indexed stack and indexes into an empty test collection
- [x] 1.3 Add `scripts/dev.sh` (infra via compose, stop app services and lb, run mcp-retrieval, api and vite in the foreground with prefixed output and a cleanup trap); verify the three processes start and answer, and Ctrl-C stops them

## 2. Makefile

- [x] 2.1 Write the root `Makefile` (GNU Make 3.81 compatible): default `all`, lifecycle, data, quality, dev, doctor and help targets, `require-*` checks, and variables per the design; verify `make help` lists every target, `make -n all` prints the expected command chain, and `make nonexistent` fails
- [x] 2.2 Implement `doctor` (Docker+compose, .NET SDK matching global.json, Node/npm, make version, `OLLAMA_API_KEY` set/missing without the value); verify output with the key set and with it unset (`env -u OLLAMA_API_KEY make doctor`)
- [x] 2.3 Implement `clean` confirmation (`FORCE=1` skips it) and make sure `down` keeps volumes; verify `make down && make` does not re-index

## 3. End-to-end verification

- [x] 3.1 From a stopped stack run plain `make`; verify all services healthy, the index present, and the URL printed, then run it again and verify there is no rebuild or re-index
- [x] 3.2 Run `make test`, `make lint`, `make verify`, `make eval-selection` and `make up API_REPLICAS=3`; verify each succeeds and three api replicas serve traffic (then restore 2)
- [x] 3.3 Verify failure propagation: a target with a failing command (e.g. `make test-web` with a temporarily broken test in a scratch copy, or `WAIT_TIMEOUT=1 make up` during startup) exits non-zero

## 4. Documentation

- [x] 4.1 Update README quick start and commands, CLAUDE.md commands, and `.vscode/tasks.json` to use the make targets; add a DECISIONS.md note on Make 3.81 compatibility; verify the sections exist
