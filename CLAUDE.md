# maf-lab

TAMP billing RAG assistant. Read openspec/project.md first — it holds the
stack, layout, and hard conventions. Active change proposals live under
openspec/changes/.

Non-negotiables while editing:
- Migration order (user decision, 2026-10-08): finish the code migration according to the agreed plan first.
  Indexing, reindexing, graph refreshes and live evals that depend on those indexes run only after that code migration
  is complete. During migration, use builds, contract tests and docs/spec checks; track the index/eval gate as
  deferred final validation and continue the planned code work. Do not start an intermediate index refresh because
  source files or their timestamps changed.
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
- Tenant (tenant_id) comes from the principal only. Never add a tenant
  parameter to a tool, an endpoint, or a query builder. The core principal
  holds core roles only; domain roles are claims the domain's server reads.
- Plugins (spec: `plugins`, guide: docs/plugins.md): the core is domain-agnostic and never references a plugin — no
  project reference, package or import from `src/` or `web/src/` into `plugins/`, and no file outside a plugin's folder
  names its path or its project (the api and the test hosts take plugin code only through `Directory.Build.targets`'
  and `import.meta.glob`'s globs). A plugin reaches Qdrant, Neo4j, its tables and AG-UI only through the core's seams
  (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); it may contribute behaviour, observers, routes and tables, but
  its tools reach the agent only through MCP. Its manifest, compose file, lb snippets, make targets, server and web
  parts live in its folder; make merges them for the installed set, and the api, copilot-runtime and the lb apply only
  `plugins/.installed` at run time.
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

Chat model: the installed `chat-model` provider selected by `MAF_CHAT_MODEL` (today `ollama-cloud`,
`gpt-oss:120b`, with `OLLAMA_API_KEY` from the environment).
Typed decisions: the installed `IDecisionEngine` (today the `jev` provider, pinned `jev-1.13.0`). Exactly one
engine must be installed. Its contract is docs/rules/jev-usage.md; Jev's `JEV_MAF_LAB` is sent only as the bearer
header, never in a prompt, state, trace or log.
Embeddings: the installed `embeddings` provider through `IEmbeddingGenerator` (today `ollama-embeddings`, local
`embeddinggemma`, vector `dense_v3`). Changing the model = new profile + `make rebuild-index FORCE=1`.
The current provider keeps two instances: `ollama` (11435) for queries, `ollama-batch` (11436) for documents,
each pinned to its CPUs. Every request carries its instance's `num_thread` (dev defaults 4/12).
`MAF_CORE_PROVIDERS` defaults to `jev ollama-cloud ollama-embeddings`; make and provider hosts reject a missing
engine or selected chat provider before work starts. Providers change with `make up`, not plugin-on/off.

Entry point: everything runs behind the nginx load balancer on http://localhost:7171 (api and compliance x2; copilot-runtime x1; with the default plugins installed, billing's mcp-retrieval and portfolio's mcp-portfolio x2 and code's mcp-code x1). With the inspector plugins installed (dev only, loopback): A2A http://localhost:7172, MCP http://localhost:7173, Redis Insight http://localhost:7174, Neo4j Browser http://localhost:7175.
Graph store: Neo4j (`neo4j:2026.09.0-community`), Bolt on 127.0.0.1:7687, password `NEO4J_PASSWORD` (dev default `maf-lab-dev-graph`).

Plugins: `make plugins` lists them, `make plugin-on NAME=` / `make plugin-off NAME=` switch one, `MAF_PLUGINS` picks the
set (unset: every bundled plugin allowed in `MAF_ENV` except `_example`; `none`: no domain plugin, only the core's
providers; otherwise a list, dependencies added; `CI_MODE=1` uses `CI_PLUGINS`; `MAF_CORE_PROVIDERS` is always
installed first). `make core` starts the core alone — no domain plugin, only the core's providers
(`MAF_PLUGINS=none` + `MAF_CORE_PROVIDERS`; every domain is a plugin): every turn declines before any model,
decision-engine or tool call, and a plain `make` brings the plugins back.

Commands: `make help`.

- Before touching any TypeSafe Jev call, read docs/rules/jev-usage.md.

Company identity foundation (adopt-company-idp, in progress): configure Auth:Authority for JWKS-backed access
tokens; stage/prod refuse the symmetric dev fallback. The API and production MCP hosts use AddLabAuthentication.
Tenant comes from exactly one organization alias and roles from realm/the designated core client. GroupIds are
provisioner-managed immutable IDs in the standard groups claim; native organization group paths are metadata.
The external Keycloak 26.8.0 templates and supported-feature gate live in compose/keycloak; run make keycloak-check
with the same feature overrides as the IdP server build. PKCE UI and real Keycloak container proof remain open.
Dev-login cannot be installed when a company Authority is configured.
Company MCP hosts require their public Auth:ResourceUri and use native SDK metadata/challenges. Forwarding is
accepted only from explicit Auth:TrustedProxyNetworks; empty configuration trusts no proxy. The external TLS
deployment must preserve public host/scheme through the final ingress. API Bearer challenges stay unchanged.
Company operators require native string sid/scope and exactly organization:<validated alias>. Core records the first
authenticated API entry per issuer/session/operator/tenant atomically with its durable session receipt, before
dispatch (including later route denial). Tenant Overview lists entry metadata independently of Compliance.
Operator entry is not a content grant. Break-glass uses an audited SQLite grant and session-bound shared Redis
permission; API and direct MCP content requests check it independently. Expiry cannot be extended. End acknowledgement
requires shared revocation and stored endedAt. Standalone Redis synchronous AOF policy is checked; failures deny content.
