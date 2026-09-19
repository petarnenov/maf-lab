# Design

## Context

See proposal.md. Every command CI needs already exists as a make target (`test-dotnet`, `test-web`, `lint`, `up`,
`verify`, `eval`). Integration tests need Docker, which GitHub's `ubuntu-latest` runners provide. What does not carry
over to CI: the chat model (Ollama Cloud needs `OLLAMA_API_KEY`, which fork PRs do not get) and model downloads plus
CPU embedding (indexing the full corpus with real `nomic-embed-text` took about 11 minutes locally).

## Goals / Non-Goals

**Goals:**
- Every push and PR runs specs, .NET, web and a full-stack e2e, with no secrets and no model downloads.
- Evals with real models are one click away, on demand.
- `make ci` reproduces the push workflow locally.

**Non-Goals:**
- Publishing images to a registry, deployments, release automation.
- Running evals on every PR (project.md: evals run on demand and on prompt/model/tool/chunking changes).
- Branch protection rules (they can be added in repository settings later).

## Decisions

### D1. Ollama-compatible stub for CI
`compose/ollama-stub/server.py` (Python stdlib, on `python:3.13-alpine`) implements the endpoints OllamaSharp calls:
- `GET /api/version`, `GET /api/tags`, `POST /api/show`.
- `POST /api/embed`: deterministic feature-hashing vectors (the same idea as the test `FakeDenseEncoder`), with
  dimensions per model (nomic-embed-text 768, all-minilm 384) so the Qdrant schema is unchanged.
- `POST /api/chat`: a streamed NDJSON answer in several chunks with small delays, quoting the first `<tool_data>`
  source when one is present in the messages; `stream:false` is supported too.

Forced retrieval (`RequiredToolModeChatClient`) issues `search_documents` without the model, so tool events, MCP,
tenancy and sources are real. *Alternative:* pull small real models in CI — slower (downloads plus CPU embeddings),
non-deterministic, and chat would still need a model that calls tools.

### D2. CI compose override
`compose/docker-compose.ci.yml` replaces the `ollama` service image with the stub (same service name and port, so
`Models__OllamaEndpoint=http://ollama:11434` stays), turns `ollama-init` into a no-op (`alpine true`), and sets
`Models__ChatEndpoint=http://ollama:11434` with `CHAT_MODEL=stub`. The `ollama` host is already exempt from the API
key requirement. The Makefile gets `CI_MODE=1` → `COMPOSE_FILES += compose/docker-compose.ci.yml`. Host-side indexing
already targets `localhost:11435`, which in CI mode is the stub.

### D3. Workflows and jobs
`ci.yml`: `on: [push, pull_request]`, `permissions: contents: read`, and `concurrency` cancelling superseded runs per
ref. Jobs (the three check jobs run in parallel; e2e needs none of them, to keep wall time low):
- **specs**: Node 24, `npx @fission-ai/openspec@<pinned> validate --all --strict`.
- **dotnet**: `actions/setup-dotnet` with `global-json-file`, NuGet cache keyed on `Directory.Packages.props` and
  `**/*.csproj`, then `make lint-dotnet test-dotnet`.
- **web**: `actions/setup-node` (Node 24, npm cache on `web/package-lock.json`), then `make test-web lint-web build-web`.
- **e2e**: setup-dotnet, then `make ci-e2e` = `up` (CI mode) → `index-if-empty` → `verify`. On failure, `make ps` and
  compose logs are written to `e2e-logs/` and uploaded as an artifact.

`evals.yml`: `workflow_dispatch` with `suite` (choice: all/selection/retrieval/generation/injection). It brings up the
normal stack (real Ollama service pulls nomic-embed-text and all-minilm; chat on Ollama Cloud with
`OLLAMA_API_KEY: ${{ secrets.OLLAMA_API_KEY }}`), runs `make index` and `make eval SUITE=…`, and uploads
`evals/reports/` as an artifact. Workflow-dispatch runs never get fork secrets, and pull_request workflows receive no
secrets.

### D4. Make targets for parity
Split `lint` into `lint-dotnet` and `lint-web` (with `lint` depending on both), add `build-web`, `ci-e2e` and `ci`
(`specs dotnet web e2e` in sequence), and add a `specs` target (`npx … openspec validate --all --strict`). All
targets run unchanged on GNU Make 4.x (Ubuntu) and 3.81 (macOS).

### D5. Pins
Actions pinned to the current major tags recorded in DECISIONS.md (checkout, setup-dotnet, setup-node, cache via the
setup actions, upload-artifact). Runner `ubuntu-24.04`, not `-latest`, for reproducibility. OpenSpec CLI pinned to the
version used locally (1.13.1).

### D6. Publishing
`gh repo create petarnenov/maf-lab --public --source . --push`, then the secret is set through stdin
(`gh secret set OLLAMA_API_KEY < <(printf %s "$OLLAMA_API_KEY")`) so the key never appears in argv or a file. The first
CI run is watched to green with `gh run watch`.

## Risks / Trade-offs

- [The stub diverges from real Ollama APIs] → only endpoints the code uses are implemented. The on-demand evals
  exercise the real APIs, and a stub regression shows up as an e2e failure, not a silent pass.
- [e2e flakiness on shared runners (timing of failover and SSE checks)] → `WAIT_TIMEOUT` is raised in CI, the verify
  script is already deterministic, and logs are uploaded on failure.
- [Public repository] → the corpus and seed data are synthetic, the key is only in secrets, and `.gitignore` excludes
  databases and caches. The author email in commit history becomes public; this is accepted by choosing a public repo.
- [Actions minutes] → public repositories get free minutes. A concurrency group cancels superseded runs.
