# Spec Delta

## MODIFIED Requirements

### Requirement: Model-free end-to-end test
The e2e job SHALL build the images, start the full stack behind the load balancer on port 7171 using an
Ollama-compatible stub for embeddings and chat that also answers intent classification requests and passage relevance
requests in Jev's request and response shape, index the sample corpus, and pass every check of `make verify`. It MUST
NOT require any secret or model download, and MUST NOT call the real Jev endpoint — neither from the api nor from the
retrieval server.

#### Scenario: Stack verified in CI
- **WHEN** the e2e job runs on a pull request from a fork (no secrets available)
- **THEN** the stack starts, the corpus is indexed, and all load-balancer checks pass, including SSE ordering and tool calls

#### Scenario: Procedural turn forced in CI
- **WHEN** the e2e job asks a procedural question
- **THEN** the stub classifies it, `search_documents` is forced, and no request leaves the runner for the Jev endpoint

#### Scenario: Diagnostics on failure
- **WHEN** the e2e job fails
- **THEN** the service status and container logs are available in the job output or as an artifact

#### Scenario: Relevance judged by the stub in CI
- **WHEN** a search runs in the e2e stack with the relevance gate on
- **THEN** the stub answers the relevance request, an in-domain question still returns documentation, and no request leaves the runner for the Jev endpoint
