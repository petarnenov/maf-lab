# Proposal

## Why

Every guarantee in maf-lab (tenant isolation, the tool contract, SSE through the balancer, the make workflow) is
verified today only when someone runs `make test` and `make verify` locally. Pushing to GitHub with Actions makes
those checks run on every push and pull request, catches "works on my Mac" drift (Linux, GNU Make 4, fresh caches),
and gives the lab a shareable, public home.

## What Changes

- Publish the repository as **public** `github.com/petarnenov/maf-lab` and push `main`.
- Add `.github/workflows/ci.yml`, which runs on every push and pull request with these jobs:
  - **specs**: `openspec validate --all --strict`.
  - **dotnet**: build with warnings as errors, and unit + integration tests (Testcontainers Qdrant).
  - **web**: lint, Vitest and build.
  - **e2e**: build the images and bring the full stack up with `make` in CI mode behind the balancer on 7171, index the
    corpus, then `make verify`.
- **CI mode needs no model and no secret:** a small Ollama-compatible stub (`compose/ollama-stub`) serves deterministic
  embeddings and a scripted streamed chat answer. Forced retrieval still calls `search_documents` for real, so SSE,
  tool calls, sources, MCP, tenancy, failover and admin jobs are all exercised end to end.
- Add `.github/workflows/evals.yml` (manual `workflow_dispatch`, suite input) that runs the real evals with local
  Ollama embeddings and Ollama Cloud chat, using the `OLLAMA_API_KEY` repository secret. Reports are uploaded as
  artifacts. This follows project.md: evals run on demand, not on every commit.
- New make targets: `make ci` (what the workflow runs locally) and `make ci-e2e`.
- Docs: a CI badge and section in the README; action and version pins in DECISIONS.md.

## Capabilities

### New Capabilities
- `continuous-integration`: which checks run on push/PR and how, the model-free CI mode, on-demand evals with a
  secret, secret handling, and caching.

### Modified Capabilities
<!-- None: runtime behaviour is unchanged; CI mode swaps only the model backend through compose configuration. -->

## Impact

- New: `.github/workflows/ci.yml`, `.github/workflows/evals.yml`, `compose/ollama-stub/` (Dockerfile + stdlib Python
  server), `compose/docker-compose.ci.yml`.
- Updated: `Makefile` (`ci`, `ci-e2e`, compose-file selection), `README.md`, `DECISIONS.md`.
- External: a public GitHub repository and one Actions secret. Git history, including author name and email, becomes
  public.
