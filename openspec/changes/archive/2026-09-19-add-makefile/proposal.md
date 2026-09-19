# Proposal

## Why

Running maf-lab today means remembering a dozen commands spread across README, CLAUDE.md and `.vscode/tasks.json`:
`docker compose -f compose/docker-compose.yml up -d --build`, `OLLAMA_MODELS_DIR=…`, host-side indexing with
`Models__OllamaEndpoint=http://localhost:11435`, `dotnet test --solution …`, `npm test -- --run`, `scripts/verify_lb.sh`,
eval flags, and more. One `make` entry point makes the lab reproducible for anyone who clones it, and gives CI and
agents a stable command surface.

## What Changes

- Add a root `Makefile`. Plain **`make` starts everything**: it checks prerequisites, builds and starts the compose stack,
  waits until every service is healthy, indexes the corpus if the index is empty, and prints the entry URL
  (http://localhost:7171).
- Targets for the whole workflow:
  - Lifecycle: `up`, `down`, `restart`, `ps`, `logs`, `clean`.
  - Data: `index`, `reindex`, `drift`, `migrate`.
  - Quality: `test`, `test-dotnet`, `test-web`, `lint`, `verify`, `eval`, `eval-<suite>`.
  - Local run without Docker: `dev`.
  - Setup: `doctor`, `help`.
- Every target accepts overridable variables (e.g. `CHAT_MODEL`, `API_REPLICAS`, `SUITE`), follows the documented
  environment (`OLLAMA_API_KEY` from the environment, never written to files), and returns a non-zero exit code on failure.
- README, CLAUDE.md and `.vscode/tasks.json` switch to the make targets.

## Capabilities

### New Capabilities
- `make-workflow`: the make command surface — default start, target catalogue and help, prerequisite checks,
  idempotency, configuration through variables, and failure behaviour.

### Modified Capabilities
<!-- None: services' behaviour is unchanged; the Makefile only drives existing commands. -->

## Impact

- New: `Makefile`, `scripts/wait_healthy.sh`, `scripts/index_if_empty.sh`.
- Updated: `README.md`, `CLAUDE.md`, `.vscode/tasks.json`.
- No new dependencies. Make must work with GNU Make 3.81, the version macOS ships.
