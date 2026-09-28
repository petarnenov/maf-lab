# Spec Delta

## MODIFIED Requirements

### Requirement: Model-free end-to-end test
The e2e job SHALL build the images, start the full stack behind the load balancer on port 7171 using an
Ollama-compatible stub for embeddings and chat that also answers intent classification requests in Jev's request and
response shape, index the sample corpus, and pass every check of `make verify`. It MUST NOT require any secret or model
download, and MUST NOT call the real Jev endpoint.

#### Scenario: Stack verified in CI
- **WHEN** the e2e job runs on a pull request from a fork (no secrets available)
- **THEN** the stack starts, the corpus is indexed, and all load-balancer checks pass, including SSE ordering and tool calls

#### Scenario: Procedural turn forced in CI
- **WHEN** the e2e job asks a procedural question
- **THEN** the stub classifies it, `search_documents` is forced, and no request leaves the runner for the Jev endpoint

#### Scenario: Diagnostics on failure
- **WHEN** the e2e job fails
- **THEN** the service status and container logs are available in the job output or as an artifact

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
