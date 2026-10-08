# maf-lab

TAMP billing RAG assistant. Read openspec/project.md first — it holds the
stack, layout, and hard conventions. Active change proposals live under
openspec/changes/.

Non-negotiables while editing:
- TOP PRIORITY: every CLI tool shows a progress bar for the work it does, and
  every UI-started process that can take longer than 3 seconds shows progress
  in the page's theme and design (spec: `progress-feedback`). Every proposal
  says how its work shows progress in a `## Progress` section; `make
  docs-check` fails one without it.
- TOP PRIORITY: everything can be stopped — Esc on the page that started it,
  Ctrl+C (and SIGTERM) in a terminal (exit 130, at a safe point, cancelling any
  server work it started). A stop travels only by the protocols' own means
  (CopilotKit stop, an aborted request, MCP's transport, A2A `tasks/cancel`);
  work that outlives its request is stopped through the store that owns its
  state, atomically, and its worker watches that store — any replica takes the
  stop, no sticky routing. A stop is shown only once the work's own state says
  so (spec: `stop-anything`). Every proposal says how its work stops in a
  `## Stopping` section; `make docs-check` fails one without it.
- TOP PRIORITY: follow SOLID and only established, widely adopted industry
  standards and practices. Prefer a named standard, protocol, RFC or pattern
  (official MCP/A2A/AG-UI, OAuth RFCs, OpenTelemetry, POSA/GoF patterns) over
  a home-grown one; never invent a format, protocol or mechanism where an
  established one exists; anything of the project's own is recorded in
  DECISIONS.md with the alternatives rejected (spec: `solid-and-standards`).
  Every proposal says what it stands on in a `## Principles` section; `make
  docs-check` fails one without it.
- Tenant (firm_id) comes from the principal only. Never add a tenant
  parameter to a tool, an endpoint, or a query builder.
- One method builds Qdrant queries and applies the tenant filter. Do not
  add another. The graph has its own one: `TenantScopedGraph.ReadAsync` runs
  a fixed Cypher template and binds the tenants; Cypher never comes from a
  request, a tool argument or the model.
- Tool results are DTOs designed for the model; never return entities.
- No message content in logs.
- Only official AG-UI events between agents and the web (agui-protocol-only): no `CUSTOM` event, no AG-UI event
  built outside `Agent/AGUI/AGUIMappings.cs`, no SSE or agent `fetch` of our own. Agents via `MapAGUIServer`; the web
  reaches them only through CopilotKit and its runtime (`copilot-runtime`, wiring only).
- If a package version must move, update DECISIONS.md in the same commit.
- Never edit inside a `generated:` block; edit its source and run `make docs`.
  A change that alters a route, target, project, model or lb location
  updates its docs in the same change; `make docs-check` (in CI) enforces it.

Chat model: `gpt-oss:120b` on Ollama Cloud — needs `OLLAMA_API_KEY` in the environment.
Intent classifier: TypeSafe Jev (`jev-1.13.0`), the only one; the same key routes data turns (api), judges search
relevance (mcp-retrieval) and grades answers in the eval (`generation`, `generation-judge`) — needs `JEV_MAF_LAB` in the environment,
sent only as the bearer header; never put it in a prompt, state, trace or log.
Embeddings: local Ollama, one multilingual model (`embeddinggemma`, vector `dense_v3`). Changing it = new profile + `make rebuild-index FORCE=1`.
Two instances serve it: `ollama` (11435) for search queries only, `ollama-batch` (11436) for document embeddings (indexing, migrate, admin index runs), each pinned to its own CPUs; every request must send that instance's `num_thread` (one without it reloads the model on all CPUs).

Entry point: everything runs behind the nginx load balancer on http://localhost:7171 (api, mcp-retrieval, mcp-portfolio and compliance x2; mcp-code and copilot-runtime x1). Inspectors (dev only, loopback): A2A http://localhost:7172, MCP http://localhost:7173, Redis Insight http://localhost:7174, Neo4j Browser http://localhost:7175.
Graph store: Neo4j (`neo4j:2026.09.0-community`), Bolt on 127.0.0.1:7687, password `NEO4J_PASSWORD` (dev default `maf-lab-dev-graph`).

Commands (see `make help`):
- `make` — start everything on http://localhost:7171 (build, wait healthy, index if empty)
- `make down` · `make ps` · `make logs SERVICE=api`
- `make test` · `make lint` · `make verify` · `make eval SUITE=selection`
- `make index` · `make graph` (billing + code graph in Neo4j) · `make drift` · `make dev` (local, no Docker for app services) · `make doctor`
- `make docs` (rewrite generated doc blocks) · `make docs-check` (docs vs code; also in `make ci`)

- Before touching any TypeSafe Jev call, read docs/rules/jev-usage.md.
- Before an OpenSpec phase or a subagent delegation, read docs/rules/openspec-models.md: model per
  phase and task class; two tries per model, then one model up, then the human.
