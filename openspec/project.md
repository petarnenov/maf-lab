# maf-lab

Learning project: a RAG-backed AI assistant for a TAMP (turnkey asset
management platform) billing domain. Its purpose is to exercise, end to
end, retrieval-as-a-tool over a multi-tenant vector store — chunking,
hybrid retrieval, tenant isolation, tool-selection evaluation, production
feedback loops, and prompt-injection defenses — in a shape close to a real
production system.

## Tech stack (latest stable at project start; pin exact versions in
## DECISIONS.md on first install and do not drift without a note)

- Backend: C# / .NET (latest LTS), ASP.NET Core minimal APIs.
- Agent framework: Microsoft Agent Framework (Microsoft.Agents.AI.* packages,
  1.x GA). Agents, AIFunction tools, MCP client integration, middleware.
- MCP server: official ModelContextProtocol C# SDK, targeting spec revision
  2026-07-28 (stateless core). If the SDK lags the spec on any feature the
  project needs, implement on top of its transport and record the gap.
- Vector store: Qdrant (latest stable, Docker image), .NET client
  Qdrant.Client. Dense + sparse named vectors, payload indexes, Query API
  with prefetch and fusion.
- Models: Ollama in Docker for local development — one embedding model
  (nomic-embed-text or equivalent) and one chat model. Provider is
  abstracted behind Microsoft.Extensions.AI so it can be switched to
  Azure OpenAI / OpenAI by configuration only.
- Sparse encoder: BM25 implemented in C# (tokenizer, IDF from the indexed
  corpus, persisted vocabulary). No external service.
- Frontend: React (latest), TypeScript, Vite, React Router, TanStack Query
  (React Query), Vitest + Testing Library. No UI framework required; plain
  CSS modules are fine.
- Containers: Docker Compose — qdrant, ollama, api, mcp-retrieval, web.
  One command brings everything up.
- Tests: xUnit for .NET (unit + integration with Testcontainers for
  Qdrant), Vitest for the web. Evals are a separate CLI project, not part
  of the unit test run.
- Dev environment: VS Code with C# Dev Kit, ESLint, Prettier. Include
  .vscode/launch.json for api + web compound debugging and
  .vscode/tasks.json for `compose up`, `index`, `eval`.

## Repository layout

```
maf-lab/
  src/
    Maf.Lab.Api/            ASP.NET Core host: agent, chat endpoints (SSE), feedback, admin
    Maf.Lab.Retrieval/      MCP server exposing search_documents; Qdrant access; BM25
    Maf.Lab.Indexing/       Console app: source loaders, chunkers, embedding, upsert
    Maf.Lab.Eval/           Console app: eval datasets, runners, metrics, report
    Maf.Lab.Domain/         Shared contracts ONLY (principal, tenant, result shapes)
  web/                      Vite + React app
  compose/                  docker-compose.yml, ollama model pull script, seed data
  data/                     Sample corpus: docs/, procedures/, code/ per tenant + shared
  evals/                    JSONL datasets: selection, retrieval, generation, injection
  openspec/
  DECISIONS.md
```

## Conventions

- Tenant is `firm_id`. It is derived from the caller's token, never from a
  request parameter, tool argument, or model output.
- Every Qdrant query goes through exactly one method that takes a
  Principal and applies the tenant filter. No other code builds queries.
- Tool results are purpose-built DTOs, never serialized entities.
- Model-facing error text never contains stack traces, SQL, or hostnames.
- Logs carry structure (tool names, latencies, counts), never message
  content. Message content lives in its own store with its own retention.
- All write operations behind the agent require explicit user
  confirmation before execution (MRTR `input_required` at the MCP layer,
  a confirmation dialog in the UI).
- Evals run on demand and on change of prompt, tool description, model, or
  tool set — not on every commit.
