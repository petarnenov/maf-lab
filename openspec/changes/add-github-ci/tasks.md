# Tasks

## 1. Model-free CI mode

- [x] 1.1 Implement `compose/ollama-stub` (Dockerfile + stdlib `server.py`: version, tags, show, embed with 768/384-d hashing vectors, streamed/non-streamed chat quoting the first tool_data source); verify with curl against the container for each endpoint
- [x] 1.2 Add `compose/docker-compose.ci.yml` (stub as `ollama`, no-op `ollama-init`, chat endpoint → stub, CHAT_MODEL=stub) and `CI_MODE=1` in the Makefile to include it; verify `make up CI_MODE=1` brings the stack healthy with no internet model pulls
- [x] 1.3 Verify end to end locally in CI mode on a fresh Qdrant volume: `make ci-e2e` indexes with stub embeddings and `make verify` passes all 17 checks (then restore the normal stack)

## 2. Make targets for parity

- [x] 2.1 Add `specs`, `lint-dotnet`, `lint-web`, `build-web`, `ci-e2e` (up + index-if-empty + verify, CI mode) and `ci` targets; verify `make help` lists them and `make ci` passes locally

## 3. Workflows

- [x] 3.1 Write `.github/workflows/ci.yml` (push + pull_request, contents: read, concurrency, jobs specs/dotnet/web/e2e with caches, ubuntu-24.04, e2e log artifact on failure); verify with actionlint
- [x] 3.2 Write `.github/workflows/evals.yml` (workflow_dispatch suite choice, real Ollama + Ollama Cloud via secret, `make index` + `make eval`, reports artifact); verify with actionlint

## 4. Publish and prove

- [x] 4.1 Create the public repo `petarnenov/maf-lab`, push main, and set the `OLLAMA_API_KEY` secret via stdin; verify `gh repo view` and `gh secret list` (name only)
- [x] 4.2 Watch the first CI run to green; fix anything Linux-specific. Verify all four jobs succeed (`gh run view`)
- [x] 4.3 Dispatch the evals workflow with `suite=selection`; verify it succeeds, the log shows the key only as masked, and the reports artifact exists

## 5. Documentation

- [x] 5.1 README CI badge + CI section (what runs where, `make ci`, how to dispatch evals), DECISIONS.md pins (actions, runner, OpenSpec CLI) and the stub decision; verify the sections exist
