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
Ollama-compatible stub for embeddings and chat that also answers intent classification requests and passage relevance
requests in Jev's request and response shape, index the sample corpus, and pass every check of `make verify`. It MUST
NOT require any secret or model download, and MUST NOT call the real Jev endpoint — neither from the api nor from the
retrieval server. The e2e stack SHALL run as its own compose project with its own data volumes, and MUST NOT read or
write the local development stack's data. When the e2e run passes, it SHALL remove its project and volumes. When it
fails, it SHALL leave them running for inspection.

#### Scenario: Stack verified in CI
- **WHEN** the e2e job runs on a pull request from a fork (no secrets available)
- **THEN** the stack starts, the corpus is indexed, and all load-balancer checks pass, including SSE ordering and tool calls

#### Scenario: Procedural turn forced in CI
- **WHEN** the e2e job asks a procedural question
- **THEN** the stub classifies it, `search_documents` is forced, and no request leaves the runner for the Jev endpoint

#### Scenario: Diagnostics on failure
- **WHEN** the e2e job fails
- **THEN** the e2e project's service status and container logs are available in the job output or as an artifact

#### Scenario: Relevance judged by the stub in CI
- **WHEN** a search runs in the e2e stack with the relevance gate on
- **THEN** the stub answers the relevance request, an in-domain question still returns documentation, and no request leaves the runner for the Jev endpoint

#### Scenario: Local e2e leaves the dev data alone
- **WHEN** a developer runs `make ci-e2e` while the development stack is running, then runs `make`
- **THEN** the development stack's coverage snapshots, test-generation runs, chat history and index are as they were before, and no coverage snapshot names a commit that exists only in the e2e clone

#### Scenario: Passing e2e cleans up
- **WHEN** `make ci-e2e` passes
- **THEN** the e2e project's containers and volumes are removed and ports are free for `make`

### Requirement: On-demand evals
A separate workflow SHALL run the eval suites on manual dispatch, with a suite input (default `all`), using real
embeddings, the Ollama Cloud chat model with the `OLLAMA_API_KEY` repository secret, and Jev intent classification with
the `JEV_MAF_LAB` repository secret. It SHALL upload the eval reports as an artifact and fail when a suite is below its
thresholds.

#### Scenario: Manual eval run
- **WHEN** a maintainer dispatches the evals workflow with suite `selection`
- **THEN** the selection eval runs against the stack, its turns are classified by Jev, and its JSON and Markdown reports are uploaded

#### Scenario: Jev secret missing
- **WHEN** the evals workflow runs without the `JEV_MAF_LAB` secret
- **THEN** it fails before running any suite, with a message naming the missing secret and without printing any secret

### Requirement: Secret handling
Secrets MUST only be read from GitHub Actions secrets through environment variables, MUST NOT be printed, and MUST NOT
be available to workflows triggered by pull requests from forks. No workflow SHALL write a secret to a file in the
workspace.

#### Scenario: Key not logged
- **WHEN** the evals workflow runs
- **THEN** the job log shows the key only as masked and no step prints its value

### Requirement: Local parity and caching
`make ci` SHALL run the same checks as the push workflow locally, including `make docs-check`. Workflows SHALL cache
NuGet and npm dependencies keyed on their lock/props files.

#### Scenario: Local CI
- **WHEN** a developer runs `make ci`
- **THEN** spec validation, the documentation check, .NET tests, web checks and the model-free e2e run in sequence and the exit code reflects the result

### Requirement: Documentation check on every push
The push workflow SHALL run `make docs-check` in the specs job, on every push and pull request. The workflow run MUST
fail when the check fails. The check MUST NOT need secrets, models, Docker or the .NET SDK.

#### Scenario: Doc left behind fails the run
- **WHEN** a pull request adds an api endpoint without a row in `docs/http-api.md`
- **THEN** the specs job fails with the check's message and the workflow run fails

#### Scenario: Green main includes docs
- **WHEN** the current main branch is pushed
- **THEN** the specs job's documentation check succeeds
