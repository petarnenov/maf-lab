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
- **Indexing and eval CLIs:** `src/Maf.Lab.Indexing` builds/updates the Qdrant corpus index; `src/Maf.Lab.Eval` runs the selection/retrieval/generation/injection/confirmation/intent/guardrail/domain/presentation/answer-check/code-route suites against the running stack, and the graph-depth comparison (never gated) when named.
- **Web app (`web/`):** Vite + React + TypeScript; in local non-balancer dev it proxies `/api` and `/dev` to the API process.

<!-- generated:lb-routes — edit compose/lb/nginx.conf, then run make docs -->
| Path | Match | Served by |
|---|---|---|
| `^/[a-z0-9-]+/mcp$` | regex | the balancer itself |
| `^/[a-z0-9-]+/(a2a\|\.well-known/agent-card\.json)(/\|$)` | regex | the balancer itself |
| `/lb-health` | exact | the balancer itself |
| `/api/coverage/runs/agent` | exact | `api` |
| `/copilotkit/` | prefix | `copilot-runtime` |
| `/api/chat` | exact | `api` |
| `/api/` | prefix | `api` |
| `/dev/` | prefix | `api` |
| `/.well-known/agent-card.json` | exact | `api` |
| `/a2a` | prefix | `api` |
| `/portfolio/mcp` | exact | `mcp-portfolio` at `/mcp` |
| `/compliance` | prefix | `compliance` |
| `/v1/traces` | prefix | `otel-collector` |
| `/jaeger` | prefix | `jaeger` |
| `/` | prefix | `web` |
| `/example/mcp` | exact | `mcp-example` at `/mcp` (plugin `_example`) |
| `/mcp` | exact | `mcp-retrieval` (plugin `billing`) |
| `/code/mcp` | exact | `mcp-code` at `/mcp` (plugin `code`) |
<!-- /generated:lb-routes -->

## Key conventions in this codebase

- **Progress feedback (top priority):** every CLI tool shows a progress bar for the work it does, and every process started from the UI that can take longer than 3 seconds shows progress that matches the page's theme and design (spec: `progress-feedback`). Every proposal says how its work shows progress in a `## Progress` section (`Terminal:`, `Page:`, `None — <reason>`, or `Not yet — <reason>; follow-up: <change>`); `make docs-check` fails one without it.
- **SOLID and established standards (top priority):** follow the SOLID principles and use only established, widely adopted industry standards and practices — official protocols (MCP, A2A, AG-UI), RFCs (OAuth, JWT), OpenTelemetry, recognised patterns (POSA, GoF, ports and adapters, composition root). Name the standard or pattern a design relies on; never invent a format, protocol or mechanism where an established one exists, and record any exception in DECISIONS.md with the alternatives rejected. Checkable rules are architecture tests, not prose (spec: `solid-and-standards`). Every proposal says what it stands on in a `## Principles` section (`SOLID:` and `Standards:`, `Own: <what> — <why>; DECISIONS §<n>` for anything of its own, or `None — <reason>`); `make docs-check` fails one without it.
- **Everything can be stopped (top priority):** Esc on the page that started the work, Ctrl+C or SIGTERM in a terminal (exit 130, at a safe point, cancelling the server work it started). Only the protocols' own means carry a stop (CopilotKit stop, an aborted request, MCP's transport, A2A `tasks/cancel`, this system's cancel routes); work that outlives its request is stopped through the store that owns its state, atomically, watched by its worker, from any replica (spec: `stop-anything`). Every proposal says how its work stops in a `## Stopping` section (`Key:`, `Stop:`, `Recorded in:`, `Shown:`, or `None — <reason>`); `make docs-check` fails one without it.
- Read `openspec/project.md` first for stack/layout conventions; active proposals live under `openspec/changes/`.
- **Tenant isolation rule:** tenant (`tenant_id`) is derived from the authenticated principal only; the core principal holds core roles only (`TENANT_ADMIN`, `USER`, `READ_ONLY`), and domain roles are claims only the domain's server reads. Do not add tenant parameters to endpoints, tools, or query builders.
- **Only official AG-UI events (agui-protocol-only):** agents are Agent Framework `AIAgent`s behind `MapAGUIServer`; what this system adds is content mapped in `src/Maf.Lab.Api/Agent/AGUI/AGUIMappings.cs`, the only place an AG-UI event is built. Never emit or consume a `CUSTOM` event, and never write SSE or call an agent with `fetch`: the web reaches agents only through CopilotKit and its runtime. `AGUIProtocolOnlyTests` and ESLint enforce it.
- **Single tenant query path:** all tenant-scoped Qdrant reads go through `TenantScopedSearch.QueryAsync(...)` and `TenantFilter` (`src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs`). Do not add alternate query-building paths.
- **Single graph read path:** all Neo4j reads go through `TenantScopedGraph.ReadAsync(principal, query)` (`src/Maf.Lab.Retrieval/Graph/TenantScopedGraph.cs`), which binds `$readable` from the principal and runs one of the constant Cypher templates in `GraphTemplates`; writes go through `TenantScopedGraphMaintenance`. Every node a template matches carries `tenant_id IN $readable`. Never build Cypher from a request, a tool argument or model output.
- **Tool output shape:** MCP tools return model-facing DTOs/structured content (see `SearchDocumentsTool.Structured(...)`), never persistence entities.
- **Logging rule:** keep logs structured (tool names, timings, counts) and never log message content.
- If package versions change, update `DECISIONS.md` in the same commit.
- Never edit inside a `generated:` block; edit its source and run `make docs`. `make docs-check` (part of `make ci` and CI) fails when docs and code disagree.
- Evals are on-demand gates (not every commit). Run relevant suites when prompts, tool schema/description, model settings, toolset, or retrieval/chunking behavior changes.
