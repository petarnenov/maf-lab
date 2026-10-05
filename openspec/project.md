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
- Graph store: Neo4j Community (Docker image), the official Bolt driver
  Neo4j.Driver. One database holds the billing graph (firms, households,
  accounts, runs, fee schedules, documents) and the code graph (projects,
  files, types, methods, calls, read with Roslyn), every node tagged with
  its tenant.
- Models: one multilingual embedding model on the local Ollama in Docker
  (embeddinggemma, vector dense_v3) — two instances, one for search queries
  and one for document batches, each on its own CPUs — and the chat model on Ollama Cloud
  (gpt-oss:120b). Typed decisions go to TypeSafe Jev (jev-1.13.0). The
  provider is abstracted behind Microsoft.Extensions.AI so it can be
  switched to Azure OpenAI / OpenAI by configuration only.
- Sparse encoder: BM25 implemented in C# (tokenizer, IDF from the indexed
  corpus, persisted vocabulary). No external service.
- Frontend: React (latest), TypeScript, Vite, React Router, TanStack Query
  (React Query), Vitest + Testing Library. No UI framework required; plain
  CSS modules are fine. Agents are reached only through CopilotKit
  (`@copilotkit/react-core`, headless) and its runtime, on the AG-UI
  protocol's own events (agui-protocol-only).
- Agents to browser: every agent is an Agent Framework `AIAgent` behind the
  Agent Framework's AG-UI server (`MapAGUIServer`); only official AG-UI
  events, built only by the official libraries — never a custom event.
- Containers: Docker Compose — lb (nginx, the one entry point on 7171),
  api (as the host user), api-data-init, mcp-retrieval, mcp-portfolio,
  mcp-code, compliance, test-agent, coverage-runner, web, copilot-runtime, qdrant,
  neo4j, ollama, ollama-batch, ollama-init, ollama-warm, redis, otel-collector, prometheus, jaeger, and
  the dev-only inspectors a2a-inspector, mcp-inspector, redis-insight and neo4j-browser
  (profile `inspectors`, off in CI). One command (`make`) brings
  everything up.
- Tests: xUnit for .NET (unit + integration with Testcontainers for
  Qdrant; NSubstitute for substitutes in unit tests), Vitest for the web. Evals are a separate CLI project, not part
  of the unit test run.
- Dev environment: VS Code with C# Dev Kit, ESLint, Prettier. Include
  .vscode/launch.json for api + web compound debugging and
  .vscode/tasks.json for `compose up`, `index`, `eval`.

## Repository layout

<!-- generated:repo-layout — edit each .csproj <Description> or [layout] in docs/docs-sync.toml, then run make docs -->
```
maf-lab/
  .claude/                    Agent tooling: the OpenSpec skills and commands this repository's agents use
  .github/                    GitHub Actions workflows (ci, evals) and the Copilot instructions
  .vscode/                    Compound api + web debugging, and tasks for compose up, index and eval
  compose/                    docker-compose.yml, the nginx load balancer, the Ollama stub for CI, OpenTelemetry config, seed data
  copilot-runtime/            CopilotKit's runtime (Node), wiring only: the web reaches the api's AG-UI agents through it; the AG-UI conformance check
  data/                       Sample corpus: docs/, procedures/, code/ per tenant + shared
  data-portfolio/             Portfolio domain corpus, same tenant layout, indexed into its own collection
  docs/                       HTTP API, trace events, telemetry and shared-state references; rules; screenshots; docs-sync.toml
  evals/                      JSONL datasets per suite, the accepted baseline, and the run reports
  openspec/                   Specs, active changes and the archive; project.md is the source for the OpenSpec context
  scripts/                    The multi-line logic behind make targets (bash, and Python for docs and corpus stats)
  src/                        .NET projects, one per service or shared library
    Maf.Lab.A2A/              A2A code both agents share: partner identity, the signed card, the 1.0 wire format, the request handler
    Maf.Lab.Api/              ASP.NET Core host: the agents behind the Agent Framework's AG-UI server (MapAGUIServer), history, feedback, admin, A2A
    Maf.Lab.CodeSearch/       MCP server over the repository itself: search_codebase, ask_codebase (own collection)
    Maf.Lab.ComplianceAgent/  The compliance reviewer: a second agent, an A2A server under /compliance
    Maf.Lab.CoverageRunner/   The coverage runner: builds and tests a commit plus a diff with coverage, with no secrets and no egress
    Maf.Lab.Domain/           Shared contracts ONLY (principal, tenant, result shapes)
    Maf.Lab.Eval/             Console app: eval datasets, runners, metrics, report
    Maf.Lab.Hosting/          What every service runs: instance identity and /health, telemetry, shared state
    Maf.Lab.Indexing/         Console app: source loaders, chunkers, embedding, upsert, drift
    Maf.Lab.Portfolio/        MCP server for the portfolio domain: search_portfolio_documents (own collection), household read tools
    Maf.Lab.Retrieval/        MCP server exposing search_documents; Qdrant access; BM25; the Jev relevance judge
    Maf.Lab.TestAgent/        The test-generation agent: an A2A server that writes tests for one file until it reaches a coverage target
    Maf.Lab.TestGen/          What the api, the test agent and the runner share: wire contracts, the test-path allowlist, guardrails, Cobertura, git
  tests/                      xUnit unit tests and Testcontainers integration tests
  tools/                      Developer tools outside the running stack
    Maf.Lab.A2AProbe/         A2A conformance probe: builds an agent from the card alone (make eval-a2a)
    screenshots/              Playwright script that captures the README screenshots in both themes
  web/                        Vite + React app
```
<!-- /generated:repo-layout -->

## Conventions

- **Progress feedback (top priority).** Every CLI tool shows a progress bar
  for the work it does, and every process started from the UI that can take
  longer than 3 seconds shows progress that matches the page's theme and
  design. No work runs silently. Every proposal says how what it adds shows
  progress in a `## Progress` section (`Terminal:`, `Page:`, `None — <reason>`,
  or `Not yet — <reason>; follow-up: <change>`); `make docs-check` fails one
  without it. Details: the `progress-feedback` spec.
- **Everything can be stopped (top priority).** Esc on the page that started
  the work stops it; Ctrl+C or SIGTERM stops a CLI tool at a safe point (exit
  130), cancelling the server work it started. A stop uses only the protocols'
  own means — CopilotKit's stop, an aborted request (and from it the
  CancellationToken into MCP, Qdrant and Neo4j), A2A `tasks/cancel`, this
  system's own cancel routes. Work that outlives its request is stopped through
  the store that owns its state (Redis for agent tasks, the api's SQLite for its
  jobs and tasks): a terminal state written atomically and never overwritten,
  watched by the worker, taken by any replica. A page says "Stopping…" until the
  work's own state says it stopped. Every proposal says how what it adds is
  stopped in a `## Stopping` section (`Key:`, `Stop:`, `Recorded in:`,
  `Shown:`, or `None — <reason>`); `make docs-check` fails one without it.
  Details: the `stop-anything` spec.
- Tenant is `firm_id`. It is derived from the caller's token, never from a
  request parameter, tool argument, or model output.
- Every Qdrant query goes through exactly one method that takes a
  Principal and applies the tenant filter. No other code builds queries.
- Every graph read goes through exactly one method that takes a Principal
  and one of a fixed set of Cypher templates, and binds the readable
  tenants itself. Cypher never comes from a request, a tool argument or the
  model.
- Tool results are purpose-built DTOs, never serialized entities.
- Model-facing error text never contains stack traces, SQL, or hostnames.
- Logs carry structure (tool names, latencies, counts), never message
  content. Message content lives in its own store with its own retention.
- All write operations behind the agent require explicit user
  confirmation before execution (MRTR `input_required` at the MCP layer,
  a confirmation dialog in the UI).
- Evals run on demand and on change of prompt, tool description, model, or
  tool set — not on every commit.

## Jev (TypeSafe System One)

Used for typed decisions: intent routing, guardrails, RAG passage
filtering, tool-call verification. Full rule: docs/rules/jev-usage.md.

- Core: Jev decides, code controls, LLM writes.
- Closed answer spaces only; one atomic question each.
- All questions over one state go in ONE request.
- No math, dates, or counting in Jev.
- Gate every answer on confidence scaled to risk.
- Jev is never a security boundary.
