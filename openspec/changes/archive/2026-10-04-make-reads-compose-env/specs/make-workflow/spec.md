## MODIFIED Requirements

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
