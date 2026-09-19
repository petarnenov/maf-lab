# make-workflow Specification

## Purpose
Gives maf-lab a single, discoverable command surface: `make` starts the whole lab, and every routine workflow (stop,
index, test, lint, verify, evaluate, local development) is one make target away.

## Requirements

### Requirement: Plain make starts everything
Running `make` with no target SHALL check prerequisites, build and start the full stack, wait until every service is
healthy, index the corpus when the index is empty, and print the entry URL. It MUST exit non-zero if any of these
steps fails.

#### Scenario: Fresh start
- **WHEN** a developer runs `make` on a machine with the prerequisites and no running stack
- **THEN** all services become healthy, the corpus is indexed, and `http://localhost:7171` is printed as the entry point

#### Scenario: Already running
- **WHEN** `make` is run again while the stack is up and indexed
- **THEN** it completes without rebuilding unchanged images or re-indexing, and prints the entry point

#### Scenario: Service does not become healthy
- **WHEN** a service is still unhealthy after the wait timeout
- **THEN** make prints which service is unhealthy with its recent logs and exits non-zero

### Requirement: Target catalogue and help
The Makefile SHALL provide targets for lifecycle (`up`, `down`, `restart`, `ps`, `logs`, `clean`), data (`index`,
`reindex`, `drift`, `migrate`), quality (`test`, `test-dotnet`, `test-web`, `lint`, `verify`, `eval`, and
`eval-selection`, `eval-retrieval`, `eval-generation`, `eval-injection`), local development (`dev`) and setup
(`doctor`, `help`). `make help` SHALL list every target with a one-line description.

#### Scenario: Help lists targets
- **WHEN** `make help` is run
- **THEN** every public target is listed with its description

#### Scenario: Unknown target
- **WHEN** `make nonexistent` is run
- **THEN** make exits non-zero

### Requirement: Prerequisite checks
`make doctor` SHALL report, per prerequisite, whether Docker (with compose), the .NET SDK version pinned in
`global.json`, Node/npm and GNU make are available, and whether `OLLAMA_API_KEY` is set, without printing its value.
Targets that need a missing tool SHALL fail early with a message naming the missing prerequisite.

#### Scenario: Missing API key
- **WHEN** `OLLAMA_API_KEY` is not set and `make doctor` runs
- **THEN** the report marks the key as missing and explains that chat needs it, and no key value is printed anywhere

#### Scenario: Missing tool
- **WHEN** `dotnet` cannot be found and `make test-dotnet` runs
- **THEN** it fails immediately with a message naming the .NET SDK

### Requirement: Configuration through variables
Targets SHALL accept overrides through make or environment variables, at least `CHAT_MODEL`, `API_REPLICAS`,
`MCP_REPLICAS`, `SUITE`, `OLLAMA_MODELS_DIR` and `BASE_URL`, with defaults matching the documented stack. Secrets
MUST only be read from the environment and never be written to files or echoed.

#### Scenario: Scaling through make
- **WHEN** `make up API_REPLICAS=3` is run
- **THEN** three api replicas are running behind the load balancer

### Requirement: Workflow targets delegate to the canonical commands
Quality and data targets SHALL run the same commands the project already documents: the .NET and web test suites,
lint, `scripts/verify_lb.sh`, the indexing CLI against the compose infrastructure, and the eval CLI. Their exit codes
MUST be propagated.

#### Scenario: Tests fail
- **WHEN** a test fails during `make test`
- **THEN** `make test` exits non-zero

#### Scenario: Verify the running stack
- **WHEN** `make verify` is run against a running stack
- **THEN** it runs the load-balancer verification and exits with its result

### Requirement: Safe cleanup
`make down` SHALL stop the stack and keep the data volumes. Only `make clean` SHALL remove volumes and build outputs,
and it MUST ask for confirmation unless `FORCE=1` is given.

#### Scenario: Down keeps the index
- **WHEN** `make down` and then `make` are run
- **THEN** the corpus is not re-indexed, because the Qdrant volume was kept
