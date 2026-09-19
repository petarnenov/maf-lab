# continuous-integration Specification

## Purpose
Runs maf-lab's automated checks on GitHub Actions for every push and pull request, without models or secrets, and
offers the model-based evals as an on-demand workflow.

## Requirements

### Requirement: Checks on every push and pull request
A workflow SHALL run on every push to any branch and on every pull request, with independent jobs for spec
validation, .NET build and tests, web lint/test/build, and an end-to-end stack test. The workflow run MUST fail if any
job fails.

#### Scenario: Green main
- **WHEN** the current main branch is pushed
- **THEN** the specs, dotnet, web and e2e jobs all succeed

#### Scenario: Broken test fails the run
- **WHEN** a commit makes a .NET or web test fail
- **THEN** the corresponding job and the workflow run fail

### Requirement: .NET job
The .NET job SHALL use the SDK version pinned in `global.json`, build the solution with warnings as errors, and run
unit and integration tests, including the Testcontainers-based Qdrant tests.

#### Scenario: Integration tests run in CI
- **WHEN** the dotnet job runs
- **THEN** the integration tests start `qdrant/qdrant:v1.19.1` through Testcontainers and pass

### Requirement: Model-free end-to-end test
The e2e job SHALL build the images, start the full stack behind the load balancer on port 7171 using an
Ollama-compatible stub for embeddings and chat, index the sample corpus, and pass every check of `make verify`. It
MUST NOT require any secret or model download.

#### Scenario: Stack verified in CI
- **WHEN** the e2e job runs on a pull request from a fork (no secrets available)
- **THEN** the stack starts, the corpus is indexed, and all load-balancer checks pass, including SSE ordering and tool calls

#### Scenario: Diagnostics on failure
- **WHEN** the e2e job fails
- **THEN** the service status and container logs are available in the job output or as an artifact

### Requirement: On-demand evals
A separate workflow SHALL run the eval suites on manual dispatch, with a suite input (default `all`), using real
embeddings and the Ollama Cloud chat model with the `OLLAMA_API_KEY` repository secret. It SHALL upload the eval reports
as an artifact and fail when a suite is below its thresholds.

#### Scenario: Manual eval run
- **WHEN** a maintainer dispatches the evals workflow with suite `selection`
- **THEN** the selection eval runs against the stack and its JSON and Markdown reports are uploaded

### Requirement: Secret handling
Secrets MUST only be read from GitHub Actions secrets through environment variables, MUST NOT be printed, and MUST NOT
be available to workflows triggered by pull requests from forks. No workflow SHALL write a secret to a file in the
workspace.

#### Scenario: Key not logged
- **WHEN** the evals workflow runs
- **THEN** the job log shows the key only as masked and no step prints its value

### Requirement: Local parity and caching
`make ci` SHALL run the same checks as the push workflow locally. Workflows SHALL cache NuGet and npm dependencies
keyed on their lock/props files.

#### Scenario: Local CI
- **WHEN** a developer runs `make ci`
- **THEN** spec validation, .NET tests, web checks and the model-free e2e run in sequence and the exit code reflects the result
