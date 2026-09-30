## MODIFIED Requirements

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
