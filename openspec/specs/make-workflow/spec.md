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
`reindex`, `drift`, `migrate`), quality (`test`, `test-dotnet`, `test-web`, `lint`, `verify`, `eval`,
`eval-selection`, `eval-retrieval`, `eval-generation`, `eval-injection`, and `coverage`), documentation (`docs`,
`docs-check`), local development (`dev`) and setup (`doctor`, `help`). `make help` SHALL list every target with a
one-line description, and README SHALL carry the same list, generated from the same descriptions. `make coverage`
SHALL refresh the coverage snapshot at `main`'s commit through the running stack. It SHALL exit non-zero if the
stack is not up or the refresh fails.

#### Scenario: Help lists targets
- **WHEN** `make help` is run
- **THEN** every public target is listed with its description

#### Scenario: README lists the same targets
- **WHEN** `make docs-check` runs
- **THEN** it fails unless README's target list has exactly the targets and descriptions `make help` prints

#### Scenario: Documentation targets need no stack
- **WHEN** `make docs` or `make docs-check` runs on a machine with Python 3 and without Docker or the .NET SDK
- **THEN** it completes without starting any container or building any project

#### Scenario: Unknown target
- **WHEN** `make nonexistent` is run
- **THEN** make exits non-zero

#### Scenario: Coverage refresh
- **WHEN** `make coverage` is run with the stack up
- **THEN** a new official snapshot for both toolchains is ingested and the target exits zero

#### Scenario: Coverage without the stack
- **WHEN** `make coverage` is run with the stack down
- **THEN** it exits non-zero and says the stack is not running

### Requirement: Prerequisite checks
`make doctor` SHALL report, per prerequisite, whether Docker (with compose), the .NET SDK version pinned in
`global.json`, Node/npm and GNU make are available, whether `OLLAMA_API_KEY` and `JEV_MAF_LAB` are set, whether
`MAF_LAB_REPO` names a git repository, and whether the optional `GITHUB_ISSUES_TOKEN` is set, without printing any
secret value. Targets that need a missing tool SHALL fail early with a message naming the missing prerequisite.

#### Scenario: Missing API key
- **WHEN** `OLLAMA_API_KEY` is not set and `make doctor` runs
- **THEN** the report marks the key as missing and explains that chat needs it, and no key value is printed anywhere

#### Scenario: Missing Jev key
- **WHEN** `JEV_MAF_LAB` is not set and `make doctor` runs
- **THEN** the report marks it as missing and explains that intent classification needs it, and no key value is printed anywhere

#### Scenario: Missing tool
- **WHEN** `dotnet` cannot be found and `make test-dotnet` runs
- **THEN** it fails immediately with a message naming the .NET SDK

#### Scenario: No issue token
- **WHEN** `GITHUB_ISSUES_TOKEN` is not set and `make doctor` runs
- **THEN** the report marks it as optional and missing, and says that suspected bugs will be skipped without a GitHub
  issue

### Requirement: Configuration through variables
Targets SHALL accept overrides through make or environment variables, at least `CHAT_MODEL`, `API_REPLICAS`,
`MCP_REPLICAS`, `SUITE`, `OLLAMA_MODELS_DIR` and `BASE_URL`, with defaults matching the documented stack. When the
machine-local, git-ignored `compose/.env` exists, make SHALL also take its `KEY=value` lines as variables. Make SHALL
export them to every command it runs, so compose, the scripts and the host-side CLIs see the same values. A variable
set in the environment or on the make command line SHALL take precedence over the file. Values SHALL be taken
literally, including inner spaces. Secrets MUST only be read from the environment and never be written to files or
echoed. Make SHALL NOT take a secret from `compose/.env`: `JEV_MAF_LAB`, or any name ending in `_KEY`, `_TOKEN`,
`_SECRET` or `_PASSWORD`.

#### Scenario: Scaling through make
- **WHEN** `make up API_REPLICAS=3` is run
- **THEN** three api replicas are running behind the load balancer

#### Scenario: Machine-local values reach the host CLIs
- **WHEN** `compose/.env` sets `OLLAMA_BATCH_CPUS=4-7` and `OLLAMA_BATCH_THREADS=4`, and no such variable is in the
  environment
- **THEN** the batch instance runs on CPUs 4-7, and the host-side CLIs that make starts send it a thread count of 4

#### Scenario: The environment and the command line win
- **WHEN** `compose/.env` sets `OLLAMA_BATCH_THREADS=4`, and `OLLAMA_BATCH_THREADS=7` is in the environment or on the
  make command line
- **THEN** make and the commands it runs use 7

#### Scenario: No file
- **WHEN** `compose/.env` does not exist
- **THEN** make uses the environment and the documented defaults, as before

#### Scenario: A secret in the file
- **WHEN** `compose/.env` contains `OLLAMA_API_KEY=…` and the environment does not set it
- **THEN** make does not set or export `OLLAMA_API_KEY`, and no value from the file is printed

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

### Requirement: Index the codebase
`make index-code` SHALL index the repository into the codebase collection. Documents that did not change SHALL be
skipped. `make index` and `make reindex` SHALL include the codebase. `make` SHALL index the codebase when its
collection is missing or empty, as it does for the other corpora. `make dev` SHALL run the codebase server locally.

#### Scenario: First start
- **WHEN** `make` runs with an empty codebase collection
- **THEN** the repository is indexed into it before the banner is printed

#### Scenario: Re-run
- **WHEN** `make index-code` runs again with no file changed
- **THEN** every document is reported unchanged and nothing is re-embedded

### Requirement: Verification can be repeated
`make verify` SHALL leave the business data of the stack it checks as it found it. A check that writes, such as a
confirmed fee adjustment, SHALL be undone by the same run through the same product path, so that running
`make verify` again against the same stack gives the same result. The order of a write and its reversal SHALL be
chosen so that neither is refused by the product's own rules while the data is in a valid state.

#### Scenario: Two runs in a row
- **WHEN** `make verify` passes and is run again against the same stack
- **THEN** it passes again

#### Scenario: The fee is back where it started
- **WHEN** `make verify` has applied its fee adjustment and the reversal
- **THEN** the account's fee equals the fee before the run

### Requirement: Image builds reuse downloaded packages
Building the stack's .NET images SHALL fetch each NuGet package from the network at most once per machine. Later
builds of any image SHALL take packages they already have from a persistent local cache. This SHALL hold when
several images are built in parallel. A rebuild after a source-only change MUST NOT download packages again. The
images SHALL contain the same content they would contain without the cache. An image that needs packages at run
time SHALL still carry them inside the image. Clearing the Docker build cache SHALL be the only way to drop the
package cache, and after it the next build MUST succeed by downloading again.

#### Scenario: Rebuild after a source edit
- **WHEN** a developer edits a `.cs` file under `src/` and runs `make` again with the machine offline from nuget.org
- **THEN** every .NET image rebuilds and the stack becomes healthy, with no package download attempted

#### Scenario: Parallel builds share one download
- **WHEN** `make` builds all .NET images in parallel on a machine whose package cache is empty
- **THEN** every build succeeds, and a package several images reference is downloaded once

#### Scenario: Runner stays offline-capable
- **WHEN** the coverage-runner image is built from the shared cache and then started with `--network none`
- **THEN** it restores, builds and tests a workspace as before, because the packages it needs are inside the image

#### Scenario: Cache cleared
- **WHEN** a developer runs `docker builder prune` and then `make`
- **THEN** the build downloads the packages again and succeeds

### Requirement: Index targets run the built indexer
`make index`, `index-portfolio`, `index-code`, `reindex`, `drift`, `rebuild-index` and `migrate` SHALL run the indexer
from its build output rather than through `dotnet run`, and SHALL build it first only when a source, project or build
file of the indexer or of a project it references is newer than that output.

#### Scenario: Nothing changed
- **WHEN** `make index` runs twice in a row with no source and no corpus file changed
- **THEN** the second run does not build the indexer and each corpus finishes in about a second

#### Scenario: Indexer source changed
- **WHEN** a file under `src/Maf.Lab.Indexing` or `src/Maf.Lab.Retrieval` changed since the last build
- **THEN** the target builds the indexer once before indexing

### Requirement: Index targets start their infrastructure
`make infra` SHALL start only the services the host-side indexer needs — Qdrant, the graph store, both compose
embedding instances (interactive and batch), and the one-shot model pull that makes the embedding model present and
loaded in both — and SHALL wait until they are healthy, exiting non-zero with the failing service named if they do not
become healthy. It SHALL NOT start, stop, rebuild or scale any application service. `make index`, `index-portfolio`,
`index-code`, `reindex`, `drift`, `rebuild-index` and `migrate` SHALL run `make infra` first, so each works from a
stopped stack, and SHALL embed documents through the batch instance. The target SHALL behave the same on macOS (GNU
Make 3.81, bash 3.2, Docker Desktop) and Linux (Docker Engine with the compose plugin), and SHALL show progress while
it waits, per `progress-feedback`.

#### Scenario: Stack stopped
- **WHEN** the stack is down (`make down`, or a fresh boot) and `make index` runs
- **THEN** Qdrant and both embedding instances are started and become healthy, the embedding model is pulled if
  missing, and every corpus is indexed through the batch instance; the application services stay stopped

#### Scenario: Stack already up
- **WHEN** the full stack is running and `make index` runs
- **THEN** `make infra` changes no container and finishes in about a second, and indexing proceeds as before

#### Scenario: Infrastructure cannot start
- **WHEN** Docker is not running, or Qdrant or either embedding instance does not become healthy
- **THEN** the target exits non-zero before the indexer runs, naming Docker or the unhealthy service

#### Scenario: macOS
- **WHEN** a developer on macOS with Docker Desktop and the system make (3.81) runs `make index` from a stopped stack
- **THEN** it behaves as on Linux

### Requirement: The stack writes to the host as the user who runs make
Every service that writes into a directory of the host — the repository, `data/` and `evals/` — SHALL run as the
user and group that run `make`. `make` SHALL pass that user's numeric id and group id to compose as `MAF_LAB_UID`
and `MAF_LAB_GID`, overridable like any other variable; compose used without `make` SHALL default them to `1000`.
Named volumes such a service writes SHALL be made writable for that user before it starts, including volumes an
earlier version created as root. The behaviour on Docker Desktop for macOS, which already maps bind mounts to the
host user, SHALL not change.

#### Scenario: A Linux host
- **WHEN** a developer with uid 1000 runs `make` on Linux and the stack writes into the repository, `data/` or `evals/`
- **THEN** every file and directory it creates or changes there is owned by uid 1000 and the developer's group, none by root

#### Scenario: A volume from an earlier version
- **WHEN** the api's named data volume holds files owned by root from before this change
- **THEN** `make up` hands them to the user before the api starts, and the api starts healthy and can write its data

#### Scenario: Continuous integration
- **WHEN** `make ci-e2e` runs on a CI runner whose user is uid 1001
- **THEN** the api runs as uid 1001 and the end-to-end run, including accepting a test-generation run, passes

### Requirement: make up repairs root-owned leftovers
Before starting the stack, `make up` SHALL look for paths owned by root (uid 0) under the checkout and under the
mounted repository. Only when it finds any SHALL it change the owner of exactly those paths to the user and group
that run `make`, and it SHALL print how many it repaired. It MUST NOT change any path owned by another user, MUST NOT
follow symbolic links, and MUST do nothing when `make` itself runs as root. It SHALL end with one line saying whether
anything was repaired, per the `progress-feedback` rule.

#### Scenario: Leftovers from a root api
- **WHEN** earlier runs left root-owned refs under `.git/refs/heads/test-agent/` and a root-owned test file in the working tree
- **THEN** `make up` reports how many paths it repaired, they are owned by the user afterwards, and `git commit` works again

#### Scenario: Nothing to repair
- **WHEN** nothing under the checkout or the repository is owned by root
- **THEN** `make up` changes no owner, starts no extra container for it, and says there was nothing to repair

#### Scenario: Another user's files
- **WHEN** a path under the checkout is owned by a user other than root and other than the one running `make`
- **THEN** its owner is left unchanged
