# Copilot instructions for `maf-lab`

## Build, test, and lint commands

Use `make` targets as the default interface for local and CI-like workflows.

```bash
make                     # full stack on http://localhost:7171 (build, wait healthy, index if empty)
make down                # stop stack
make ps                  # container state + health
make logs SERVICE=api    # follow logs for one service

make test                # .NET + web tests
make test-dotnet         # dotnet tests only (unit + integration)
make test-web            # vitest suite only

make lint                # .NET warnings-as-errors + web lint/prettier checks
make lint-dotnet
make lint-web

make verify              # end-to-end checks through the load balancer
make specs               # OpenSpec validation (strict)
make eval SUITE=selection
```

Single-test examples:

```bash
# .NET (single test or test class)
dotnet test tests/Maf.Lab.Tests/Maf.Lab.Tests.csproj --filter "FullyQualifiedName~TenancyTests"
dotnet test tests/Maf.Lab.IntegrationTests/Maf.Lab.IntegrationTests.csproj --filter "FullyQualifiedName~TenancyAcceptanceTests"

# Web (single file, or single test by name)
cd web && npm test -- --run src/chat/ChatPage.test.tsx
cd web && npm test -- --run src/chat/ChatPage.history.test.tsx -t "restores"
```

Runtime note: chat uses Ollama Cloud and needs `OLLAMA_API_KEY` in the environment; intent classification uses
TypeSafe Jev and needs `JEV_MAF_LAB` (sent only as the bearer header).
MCP note: workspace MCP server config lives in `.mcp.json` (Playwright server via `npx @microsoft/mcp-server-playwright`).

## High-level architecture

- **Single entry point:** nginx load balancer on `http://localhost:7171` routes everything.
- **Routing model:** the table below, generated from `compose/lb/nginx.conf`.
- **API service (`src/Maf.Lab.Api`):** ASP.NET Core host for chat SSE, history, feedback, admin/compliance surfaces, A2A protocol, and topology/trace endpoints. Persists app state in SQLite.
- **Retrieval service (`src/Maf.Lab.Retrieval`):** MCP server exposing `search_documents` + billing-related tools over `/mcp`; retrieval runs against Qdrant (dense+sparse hybrid).
- **Indexing and eval CLIs:** `src/Maf.Lab.Indexing` builds/updates the Qdrant corpus index; `src/Maf.Lab.Eval` runs the selection/retrieval/generation/injection/confirmation/intent/guardrail/domain/presentation/answer-check suites against the running stack.
- **Web app (`web/`):** Vite + React + TypeScript; in local non-balancer dev it proxies `/api` and `/dev` to the API process.

<!-- generated:lb-routes — edit compose/lb/nginx.conf, then run make docs -->
| Path | Match | Served by |
|---|---|---|
| `/lb-health` | exact | the balancer itself |
| `/api/chat` | exact | `api` |
| `/api/` | prefix | `api` |
| `/dev/` | prefix | `api` |
| `/.well-known/agent-card.json` | exact | `api` |
| `/a2a` | prefix | `api` |
| `/mcp` | exact | `mcp-retrieval` |
| `/portfolio/mcp` | exact | `mcp-portfolio` at `/mcp` |
| `/code/mcp` | exact | `mcp-code` at `/mcp` |
| `/compliance` | prefix | `compliance` |
| `/v1/traces` | prefix | `otel-collector` |
| `/jaeger` | prefix | `jaeger` |
| `/` | prefix | `web` |
<!-- /generated:lb-routes -->

## Key conventions in this codebase

- **Progress feedback (top priority):** every CLI tool shows a progress bar for the work it does, and every process started from the UI that can take longer than 3 seconds shows progress that matches the page's theme and design (spec: `progress-feedback`).
- Read `openspec/project.md` first for stack/layout conventions; active proposals live under `openspec/changes/`.
- **Tenant isolation rule:** tenant (`firm_id`) is derived from the authenticated principal only. Do not add tenant parameters to endpoints, tools, or query builders.
- **Single tenant query path:** all tenant-scoped Qdrant reads go through `TenantScopedSearch.QueryAsync(...)` and `TenantFilter` (`src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs`). Do not add alternate query-building paths.
- **Tool output shape:** MCP tools return model-facing DTOs/structured content (see `SearchDocumentsTool.Structured(...)`), never persistence entities.
- **Logging rule:** keep logs structured (tool names, timings, counts) and never log message content.
- If package versions change, update `DECISIONS.md` in the same commit.
- Never edit inside a `generated:` block; edit its source and run `make docs`. `make docs-check` (part of `make ci` and CI) fails when docs and code disagree.
- Evals are on-demand gates (not every commit). Run relevant suites when prompts, tool schema/description, model settings, toolset, or retrieval/chunking behavior changes.
