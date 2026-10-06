# Proposal

## Why

maf-lab is a billing AI assistant, but most of what runs today is the lab around it. About two thirds of the web
(roughly 10.4k of 15.7k lines outside tests) is developer tooling: the "Behind the scenes" monitor and its seven tabs,
code snippets, coverage and test generation, evals, topology, telemetry, Jev and intent statistics, the A2A and
compliance admin screens, and the curriculum. The stack has the same shape: mcp-code and the code graph, test-agent,
coverage-runner, the compliance agent, the observability trio and four inspectors. All of it starts with plain `make`,
and none of it can be left out:

- `lb` waits for every dev service to be healthy (`compose/docker-compose.yml` lb `depends_on`), and nginx refuses to
  start when one of its static upstreams (`compose/lb/nginx.conf`) does not resolve;
- `api` waits for `mcp-code`, and `MAF_LAB_REPO` is required even when coverage is not wanted;
- `Program.cs` registers every lab module unconditionally; `ChatTurnRunner`, `Domains`, the Jev routers, the guard and
  the answer check hold the codebase and portfolio domains as code;
- the web wires every lab feature with static imports in `App.tsx`, `Layout.tsx`, `ChatPage.tsx` and `chatReducer.ts`,
  and the chat state itself carries the monitor's trace store;
- `copilot-runtime` hard-codes the `testgen` agent; `verify_lb.sh`, `index_if_empty.sh` and the CI e2e job assume every
  service exists.

There is no way to run the assistant alone, no way to switch one tool off, and no way for someone else to add a tool
without editing the core in a dozen places.

The code is closer to modular than the wiring suggests. `McpToolSource` already skips an MCP server that is not
reachable, `ComplianceConsultant` already answers "unreachable" with no `BaseUrl`, the endpoints are flat `MapXxx`
calls, the Docker build contexts are already the repository root, and compose profiles are an established mechanism
(DECISIONS §68, §75). This change gives that modularity a contract.

## What Changes

This is the second change of the universal-assistant rollout, after `rename-firm-to-tenant`. The order of what follows
(the twelve `extract-<name>-plugin` follow-ups interleaved with `introduce-provider-plugins`, `adopt-company-idp`,
`enable-plugins-per-tenant`, `document-acls`, `data-lifecycle`, `add-document-parsing` and `add-white-labeling`) is the
one the user confirmed, recorded in tasks 8.1.

This change makes plugins installable per deployment. Per-tenant allowance and enablement, identity, document
permissions and the data lifecycle each come in their own later change.

- **A plugin is one folder, `plugins/<name>/`.** It holds a manifest (`plugin.toml`) and any of these parts:
  - compose services;
  - lb snippets;
  - make targets;
  - an api module;
  - a web module;
  - its specs, docs and tests.

  Deleting the folder removes the tool. Copying a folder in adds one.
- **One switch, `MAF_PLUGINS`**, in `compose/.env` or on the command line, read by make:
  - `make` (with it unset) keeps today's lab: every bundled plugin is on.
  - `make core` starts the core with no plugin: a shell that declines every turn, for checking the core.
  - `make plugins` lists the plugins, what each brings, and whether it is on.
  - `make plugin-on NAME=` and `make plugin-off NAME=` switch one plugin and apply it to the running stack.

  make writes the resolved set to an untracked `plugins/.installed`. The api, the runtime and the lb read it, and the
  manifests, at run time from `plugins/` mounted read-only. Switching a remote or infra plugin never recreates them. An
  in-process plugin needs a rolling restart of the api replicas.
- **Plugin kinds,** so most custom tools need no C# or React at all.
  - **Remote plugins** add an agent capability over a protocol the project already speaks: an MCP server (a new domain
    of tools) or an A2A agent. It is described by the standard documents: the MCP Registry `server.json` or the A2A
    Agent Card. Its manifest adds only what no standard holds: a *domain descriptor* (tool allow-list, search tool,
    routing questions, card types, guard context, prompt fragment), scope, environments and dependencies. A remote
    plugin can be written in any language.
  - **In-process plugins** add api endpoints and web screens: a .NET project implementing `IMafPlugin` and the `IContributes*` interfaces it needs, a web module
    calling `definePlugin`, or both.
  - **Provider plugins** implement something the core needs one of. They arrive with `introduce-provider-plugins`;
    the kind is declared here so manifests can name it.
  - **Infra plugins** are stores and dev tools: `qdrant`, `neo4j`, the four inspectors.
- **The core keeps no domain.** It is a domain-agnostic assistant shell:
  - the chat agent and its generic system prompt;
  - conversation memory (create, reopen, the history the agent reads), sign-in, answer feedback (`POST /api/feedback`).
    The list of past conversations is the installation-scoped `conversation-history` plugin, installed by default,
    reaching storage through the core's `IConversationStore`;
  - typed decisions (routing, guard, answer check), which read their domains from plugins. They go behind
    `IDecisionEngine` in `introduce-provider-plugins`;
  - write confirmation;
  - the plugin system itself;
  - the shared retrieval library (BM25, `TenantScopedSearch`, `TenantScopedGraph`, the Jev relevance judge), which
    in-repository MCP servers build on;
  - Redis, the lb, copilot-runtime and the web shell.

  Every domain is a plugin, **billing included**: this lab's billing is only the first domain, and the
  first real deployment may serve a different one. Everything else is a bundled plugin too (the catalogue is in
  design.md).
- **Seams instead of imports.** The api gains:
  - `IMafPlugin` for identity, plus small `IContributes*` interfaces: services, endpoints, tables, domain behaviour,
    turn observer;
  - `IDomainDescriptor`, which replaces the static `Domains` catalogue and the codebase and portfolio branches in
    `ChatTurnRunner`;
  - `ITurnObserver`, through which the monitor gets the full trace;
  - one core-owned `MapPluginAgent`, so a plugin's AG-UI agent still goes through `AGUIMappings`.

  The web gains registries for routes, nav links, chat side panes, a chat sidebar, turn actions, monitor tabs, data cards and
  chat-run observers. A pane registry with nothing in it leaves the chat full-width.
- **Infrastructure:**
  - nginx loads `conf.d/http/*.conf` (in `http{}`) and `conf.d/server/*.conf` (in `server{}`); a plugin's snippet goes in after its services are healthy, followed by a graceful
    reload; the core reserves the plugin route shapes with `return 404`, so an absent plugin's path is not served by the
    SPA;
  - each plugin's compose file holds only its own services and is merged with `-f` only when it is installed;
  - plugin settings come from the manifests keyed by plugin name, never from indexed environment lists
    (`Agent__Servers__0/1` and the runtime's hard-coded agents go).
- **Rules enforced, not trusted.** Architecture tests:
  - fail a plugin assembly that references `QdrantClient`, `IDriver` or AG-UI types, or that core references;
  - fail a manifest without `progress` and `stopping` entries;
  - fail a plugin that registers a tool (`AIFunction`/`AITool`) in process: tools reach the agent only through MCP;
  - boot the api and the web with no plugin and with every plugin;
  - cover plugin code: the existing AG-UI and tenant-query tests widen their roots from `src/` to `plugins/*/server/`
    too, and each plugin's tests live in its folder.

  A plugin may be installed only in the environments its manifest lists. CI builds a `product` image variant without
  dev and qa plugins, and stage and prod run it.

  `make docs-check` reads plugin snippets, targets and routes. CI gains a core-only leg.
- **Write your own:**
  - `make plugin-new NAME=foo KIND=mcp|app` scaffolds a working plugin;
  - `docs/plugins.md` is the authoring guide;
  - `plugins/_example/` is a remote MCP plugin kept green by CI.
- **Phased.** This change lands the contract, the infrastructure and three extractions that prove it:
  - the four inspectors (`a2a-inspector`, `mcp-inspector`, `redis-insight`, `neo4j-browser`), each its own dev/qa plugin: infrastructure only;
  - `code`: a remote MCP domain with a chat pane and a graph;
  - `monitor`: the deepest web and api coupling.

  Each remaining tool moves in its own follow-up change, listed in design.md. A follow-up only moves code.

## Capabilities

### New Capabilities

- `plugins`: what a plugin is (folder, manifest, kinds, scope, environments); how it is installed and removed; what the
  core guarantees with no domain; the api and web seams; the rules a plugin cannot break (tenant,
  AG-UI, MCP-only tools, progress, stopping, docs); and how someone writes their own.

### Modified Capabilities

- `make-workflow`: plain `make` starts the core plus the installed plugins (MODIFIED "Plain make starts everything");
  `make core`, `make plugins`, `make plugin-on|off|new`; the codebase is indexed while the `code` plugin is installed,
  by its plugin.mk (MODIFIED "Index the codebase").
- `chat-history`: the list, rename and delete apply while `conversation-history` is installed; "New conversation"
  stays in the chat header (core), unconditional; the sidebar's duplicate is dropped (MODIFIED "Conversation list", "Titles", "Delete a conversation",
  "History in the chat screen").
- `load-balancing`: path routing for the plugin routes moves into plugin snippets, and an absent plugin's route answers
  404 (MODIFIED "Path routing" and "The codebase server behind the balancer"); the lb starts with any subset of
  plugins.
- `chat-agent`: no domain server is first; a turn fails only when every server of its selected domains is down
  (MODIFIED "Tools from every domain server").
- `injection-defense`, `answer-check`: the content battery's and the answer check's contexts are named by what they
  screen, `documents` and `code` (MODIFIED "Tool and agent results are screened before the model reads them", "A code
  answer is judged as code").
- `protocol-inspectors`, `graph-store`, `web-ui`: the inspectors are plugins, linked from the navigation while
  installed, and out of CI's plugin set (MODIFIED "The inspectors start with the stack", "The protocol inspectors open
  ready to use", "CI runs without the inspectors", "Graph browser as a dev inspector", "The main navigation links to
  the inspectors").
- `codebase-search`: the snippets endpoint and the agent's codebase tools exist while the `code` plugin is installed
  (MODIFIED "Code snippets for a chat question", "The codebase search is offered to the chat agent").

## Principles

- SOLID: the full mapping is in design.md, "Principles and patterns".
  - Single responsibility: one capability per plugin folder.
  - Open/closed: a new domain or tool is a new folder, with no core change.
  - Liskov: every domain descriptor is used the same way, with no billing special case.
  - Interface segregation: `IMafPlugin` is identity only, plus small `IContributes*` interfaces.
  - Dependency inversion: the core depends on its own abstractions and never on a plugin, and an architecture test
    enforces it.
- Standards:
  - Patterns: microkernel / plug-in architecture (POSA), ports and adapters, composition root; Strategy, Observer,
    Decorator, Registry and Chain of Responsibility (GoF).
  - Protocols: official MCP, A2A and AG-UI only. MCP plugins are described by the MCP Registry's `server.json`, and
    A2A plugins by their Agent Card.
  - Manifests are validated with JSON Schema; configuration follows 12-factor.
  - Delivery: build once and promote; architecture rules as fitness functions; strangler-fig migration.
- Own: `plugin.toml`, the manifest. No established format holds a plugin's scope, environments, dependencies, domain
  descriptor, progress and stopping, so it holds only those. Everything a standard covers stays in that standard's
  document. Nearest analogues: VS Code `package.json` `contributes`, Backstage `app-config`. DECISIONS §81 (new, written when this change is applied).
- Own: `IMafPlugin` and the `IContributes*` interfaces. Nearest analogues: Orchard Core `IStartup` per module, ABP
  `AbpModule`. They were kept over bare `AddX`/`MapX` calls so the composition root discovers plugins without naming
  them. DECISIONS §81 (new).
- Own: the web's `definePlugin` and its registries. Nearest analogues: VS Code contribution points, Backstage
  extension points. DECISIONS §81 (new).
- Own: `MAF_PLUGINS`, `MAF_ENV` and `plugins/.installed`, read at run time. This is 12-factor configuration; the
  format is our own because no standard names a plugin set. DECISIONS §81 (new).

## Progress

- Terminal: `make plugin-on|off` and `make core` show one progress bar, reusing `scripts/wait_healthy.sh`'s bar. Its
  steps are the plugin's services built, started and healthy, then the lb reload. `plugin-off` adds a step per open
  item of work it stops. An in-process plugin's api restart shows one step per replica: draining, recreating, healthy,
  reloaded. `make plugin-new` writes a short list of files and needs none.
- Page: none added by this change. Every plugin's own long work keeps the progress it shows today. A plugin manifest
  must declare its progress, and `make docs-check` fails one that does not.

## Stopping

- Key: Ctrl+C (or SIGTERM) during `make plugin-on|off|core`
- Stop: the script traps INT/TERM and stops at a safe point, which is between steps, never inside one.
  - It never edits `compose/.env`, `plugins/.installed` or `conf.d` halfway: all three are written by rename.
  - During `plugin-off` with `STOP_WORK=1`, the plugin's open work is stopped through its own store first, and the
    script waits for terminal states under an indeterminate bar. Ctrl+C during that wait exits 130 and leaves the
    issued cancels in place, since they live in the work's stores.
  - During an api restart, a stop happens between replicas, never during one. A replica is taken out of rotation
    before it drains, and it finishes its runs or records them cancelled. A run whose replica died is marked cancelled
    when it is next read. On interruption, the name-based upstream is restored.

  It exits 130.
- Recorded in: `compose/.env` and `plugins/.installed` (the installed set), `conf.d` (the routes), the stores of the
  stopped work, and Docker's own container state
- Shown: "Stopped — plugins installed: …; run `make plugin-on NAME=…` again to finish". A plugin's own work stops as its
  own `## Stopping` says, and its manifest must declare that.

## Documentation impact

- `CLAUDE.md`: a non-negotiable saying the core never imports a plugin, a plugin reaches Qdrant, Neo4j and AG-UI only
  through the core's seams, and tools reach the agent only through MCP. It also states what `make core` runs.
- `openspec/project.md`:
  - the stack and layout gain `plugins/`;
  - Conventions gain the plugin rule;
  - the container list says which services belong to which plugin.
- `docs/plugins.md` (new): the authoring guide, covering the manifest reference, both kinds, the seams, the rules,
  testing and an example.
- `docs/docs-sync.toml`: a `[layout]` line for `plugins/`, `ROUTE_SOURCES` widened to `plugins/*/server`, and a
  generated `plugins` block in README listing every plugin from its manifest.
- `README.md`:
  - the quick start mentions `make core`;
  - the make-targets and lb-routes blocks are regenerated and now include plugin targets and routes;
  - the pages list names which plugin owns each page.
- DECISIONS.md §81 covers:
  - compile-time plugins over runtime DLL loading;
  - plugin compose files that hold only their own services, with the core reading the set at run time;
  - manifests over indexed environment lists;
  - `IContributes*` kept over bare `AddX`/`MapX`;
  - why fee adjustment goes with the billing plugin.

  No package versions move: Keycloak arrives with `adopt-company-idp`.
