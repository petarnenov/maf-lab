# Design

## Context

What follows comes from reading the web, the api and the infrastructure (2026-10-05). File references are current as of
commit `51435b5`.

### Web (`web/src`)

- **Routes.** Every lab page is a static import in `App.tsx:2-14`, and its route sits at `:23-69`.
- **Nav.** The nav is a hard-coded `LINKS`/`INSPECTORS` array in `components/Layout.tsx:8-27`, shown to every role.
- **Chat pane.** `ChatPage.tsx` (706 lines) owns the right pane:
  - `PANE_TABS` at `:42-45`; the pane itself at `:415-476`;
  - the state `monitorOpen`, `paneTab`, `highlight`, `autoSwitched` and `selectedKey`;
  - five imports from `monitor/`;
  - `CodeSnippetsPanel`;
  - the "Behind the scenes" toggle and the time-travel "rewound" view inside each assistant bubble;
  - the `onOpenCode` source click.
- **Chat state.** `chat/chatReducer.ts:26,95,181-200` keeps the monitor's `TraceState` inside `ChatState`.
- **Live trace polling.** `useChatStream.ts:108,322-361` records every AG-UI frame and polls
  `/api/runs/{id}/trace` every 500 ms on every run, even with the monitor closed.
- **Reasoning on reload.** A restored turn's reasoning is read from the stored trace (`ChatPage.tsx:220`), so the core
  depends on the monitor for it.
- **Already a registry in all but name.** `chat/cards/CardView.tsx:25-40` maps `activityType` → component.
- **Agents.** Which agents exist is learned from the runtime's info, but the runtime hard-codes `['chat','testgen']`
  (`copilot-runtime/server.ts:22`).
- **Shared utility.** `evals/format.ts` (`formatDate`) is a de facto shared utility imported by five lab folders.

### Api (`src/Maf.Lab.Api`)

- **Registration.** Endpoints are flat `MapXxx` calls (`Program.cs:157-179`), and the registrations are flat
  (`:31-140`). Each lab feature has its own folder and endpoint file. Coverage and TestGen alone account for about 25
  registrations.
- **The tool set is config-driven already.** `McpToolSource` connects to `AgentOptions.AllServers()`, filters by the
  turn's domains and by each server's tool allow-list, and skips a non-billing server that is down (`ToolSource.cs:86-102`).
- **Using a domain's tools is code, not config:**
  - `Domains.cs:12-47` holds a static catalogue, `SearchTool` and `GraphTool`;
  - `DataToolRouter` and `CodeToolRouter` hold static Jev question sets;
  - `ChatTurnRunner` has codebase branches (`:155-175`, `:492-530`) and portfolio focus and summary branches (`:701`,
    `:809`, `:1056-1061`);
  - `DataCards.cs:17-19`;
  - the codebase context in `Guardrail` and `JevAnswerCheck`.

  As a result, a domain added only in config gets no forced search, no typed-decision routing and no cards, and its stored
  envelope is mislabelled as billing.
- **The trace serves two purposes:**
  - **Functional:** guard signals, the previous turn's envelope for the answer check, run-id reuse detection, and
    intent/Jev statistics.
  - **Observational:** `LiveTrace` (one Redis write per event), `TracingChatClient`'s full message capture, the prompt
    and tool-schema dump, `TraceRetrieval` diagnostics, and `RunFrameRecorder`.

  Only the observational half is the monitor's.
- **One `MafDbContext`** holds the coverage, test-generation and A2A tables, and `DatabaseInitializer` has TestGen
  backfills.
- **Project references.** Api → TestGen and Api → Indexing exist only for coverage and index admin. `EvalAgentHost`
  recomposes the api's DI by hand.
- **Rules a plugin collides with:**
  - `AGUIProtocolOnlyTests` pins AG-UI types to `Agent/AGUI/`, so `TestGenRunEndpoint` lives there today;
  - tenant isolation allows only `TenantScopedSearch`, `TenantScopedMaintenance` and `TenantScopedGraph`.

### Infrastructure

- **Startup order.** `lb.depends_on` lists every service (`:484-512`), and `api.depends_on` includes `mcp-code`
  (healthy).
- **nginx** is one file with static upstreams. A missing host stops it from loading.
- **api environment.** The api env hard-codes the code, coverage, runner, test-agent, compliance and telemetry URLs, and
  `Coverage__RepoRoot=${MAF_LAB_REPO:?}` is mandatory.
- **Indexed settings.** `Agent__Servers__0/1` are indexed, so two independent fragments would collide.
- **Makefile.** It assigns `COMPOSE_PROFILES := inspectors` hard. `index_if_empty.sh` always indexes code and builds the
  code graph. `verify_lb.sh` and `conformance.mjs` require `testgen` and compliance.
- **docs.py** reads only `Makefile` and `compose/lb/nginx.conf`, with `ROUTE_SOURCES` fixed.
- **Build contexts.** Every .NET image already builds from the repository root (`context: ..`); `web` builds from
  `../web`.

## Sequence

This change is the second of eight, each with its own proposal, applied in this order:

1. `rename-firm-to-tenant`: "firm" becomes "tenant" in the core.
2. **`introduce-plugins`** (this one): the contract, the infrastructure, domains as data, and the proof extractions
   (the four inspectors, `code`, `monitor`).
3. `introduce-provider-plugins`: the decision engine, the chat model and embeddings as provider plugins.
4. `adopt-company-idp`: Keycloak, the operator role, break-glass, and the `tenant-isolation` delta.
5. `enable-plugins-per-tenant`: allowed and enabled per tenant, admin dashboards, per-plugin audiences.
6. `document-acls`
7. `data-lifecycle`
8. `add-document-parsing`

**What "in use" means here.** Until change 5 lands, a plugin is in use when it is installed. A plugin with
`scope = "tenant"` is then in use for every tenant. The manifest declares its scope already, so change 5 adds the
per-tenant layer without touching any plugin.

## Goals / Non-Goals

**Goals**
- The core is a domain-agnostic assistant shell. With a domain plugin installed (billing today), it is a working
  assistant: chat, retrieval, history, feedback, typed decisions, write confirmation. That assistant runs with no lab
  service, route, screen or table, and no lab cost per turn (no live-trace writes, no full message capture). With no
  domain installed, the assistant declines without calling the model (5h). What `make core` runs is defined in 5x.
- Switching one plugin on or off is one command, touches no tracked file, and does not restart the api for a remote or
  infra plugin (decision 2).
- Adding a plugin is adding a folder. A remote MCP or A2A plugin needs no C# or React.
- Every project rule still holds and is checked by a test, with any set of plugins enabled.

**Non-Goals**
- Loading third-party binaries at runtime, or a plugin marketplace.
- Sandboxing untrusted plugins. An in-process plugin is trusted code reviewed like any other. A remote plugin is
  isolated by its protocol: the bearer token, the tenant from the principal, and MCP tool allow-lists.
- Moving every lab tool in this change. Only the four inspectors (5o), `code` and `monitor` move here; the rest follow.

## Decisions

### 1. A plugin is a folder with a manifest

```
plugins/<name>/
  plugin.toml          # name, kind, description, depends, progress, stopping, owned specs
  compose.yml          # its own services only (merged with -f); never changes api, lb or copilot-runtime
  lb.http.conf         # its nginx upstreams (included in http{} from conf.d/http)
  lb.server.conf       # its nginx locations (included in server{} from conf.d/server)
  plugin.mk            # its make targets (included by the Makefile)
  server/              # Maf.Lab.Plugins.<Name>.csproj — IMafPlugin (in-process only)
  web/index.ts         # definePlugin({...}) (in-process only)
  specs/  docs/  tests/
```

```toml
name        = "code"
kind        = "mcp"                       # mcp | a2a | app | provider | infra
scope       = "installation"              # tenant | installation
environments = ["dev", "qa"]
schema      = 1
description = "Search and questions over this repository; the code graph"
depends     = []
progress    = "Terminal: make index-code and make graph show a bar per file"
stopping    = "Ctrl+C stops indexing between files (exit 130)"

[domain]                                  # mcp kind: how the core uses this server's tools
id           = "codebase"
tools        = ["search_codebase", "trace_code_symbol", "change_impact"]   # the allow-list; the endpoint and the
                                                                          # full tool list come from server.json and
                                                                          # tools/list, never restated here
search_tool  = "search_codebase"
graph_tools  = ["trace_code_symbol", "change_impact"]
guard_context = "Questions about this repository's own source code are in scope."
routing      = ["Is the question about this project's own source code?"]
```

The manifest is the single source the other parts are generated from:
- make reads it for `make plugins`;
- `make docs` reads it for the README block;
- the api, copilot-runtime and the lb read the manifests at run time from `plugins/`, mounted read-only (decision 2);
- the web reads the plugins in use and their routes from `/api/plugins`.

*Rejected:* scattering the facts across csproj attributes and TypeScript. One file per plugin is what makes "copy a
folder in" work.

**Standards first (project rule: SOLID and established standards).** `plugin.toml` holds only the facts that no
established format covers: scope, environments, dependencies, the domain descriptor, progress and stopping. Everything
else uses the standard document for it, kept next to the manifest:

| Kind | Standard document | Validated by |
|---|---|---|
| `mcp` | the official MCP Registry `server.json` | its published JSON Schema |
| `a2a` | the agent's own A2A Agent Card | the A2A 1.0 schema, and the A2A probe already in the project |

The manifest itself is validated against a JSON Schema (`plugins/plugin.schema.json`), not by hand-written checks.
Recorded in DECISIONS.md, with the rejected alternative: a single home-grown manifest that restates the server and the
card.

### 2. Enabled set: `MAF_PLUGINS`, applied by merging compose files

**Resolving the set.** make resolves `MAF_PLUGINS`:
- unset means every folder under `plugins/` allowed in `MAF_ENV`, except `_example`;
- `none` means the core alone (5x).

It expands `depends` and fails on a cycle or a missing plugin. It then writes the resolved set to
`plugins/.installed`, an untracked file, by rename.

**A plugin's compose file holds only its own services.** make builds `COMPOSE_FILE` from the core file plus each
installed plugin's `compose.yml`, and starts or stops only that plugin's services. A fragment never adds environment,
`depends_on` or ports to `api`, `lb` or `copilot-runtime`, so switching a plugin never recreates them and never cuts a
chat run or an AG-UI stream (review finding 6).

**The core services read the set at run time.** `api`, `copilot-runtime` and `lb` get `plugins/` mounted read-only.
- **One reload mechanism (re-review 8).** make writes `plugins/.installed` by rename and then publishes `plugins-changed`
  on Redis. Each api and runtime replica re-reads the file and the manifests when the message arrives, and every 30
  seconds anyway, so a missed message is bounded. No file watcher is used: FileSystemWatcher over a Docker Desktop bind
  mount is unreliable. A remote or infra plugin's domain, tools and routes apply from the next turn, with no restart.
- **A malformed manifest at run time** is rejected as a whole. The replica keeps its last good set, logs the plugin's
  name, and `/api/plugins` shows that plugin as "invalid manifest".
- The api no longer `depends_on` any plugin service. It already degrades when one is absent.

**`conf.d` is derived, never state (re-review 6).** `compose/lb/conf.d/` is git-ignored and regenerated from
`plugins/.installed` by every `make up`, so a `make down` followed by a `make` with another set never leaves a snippet
whose upstream is not running. Each copy writes both parts, `http/` and `server/`, then runs
`nginx -t && nginx -s reload`. There is never a reload between the two copies.

**Switching on and off:**
- `make plugin-on` starts the plugin's services and waits until they are healthy. It then writes the snippet and
  reloads, which is graceful, then writes `.installed` and publishes.
- `make plugin-off` first stops the plugin's own open work, through the store that owns it (stop-anything). Each plugin
  with long work (coverage runs, admin jobs, A2A tasks) implements `IContributesOpenWork`:
  - `ListOpenAsync`;
  - `CancelAllAsync`, which writes `canceled` atomically to its store, so the worker that watches it stops.

  `plugin-off` lists the open work and refuses, unless `STOP_WORK=1`. With it, it cancels through the store and waits
  until every item is terminal. Only then does it remove `.installed` and the snippet, reload, and stop the services.
  A page that started that work therefore shows "stopped" from the work's own state, never a vanished container.

**In-process plugins are the exception (re-review 2, corrected by re-audit 3).** A plugin with a `server/` part
changes the api's services and routes, so switching it restarts the api replicas.

**Why neither a blind restart nor a plain drain is safe:**
- nginx resolves `api` only when it loads, so a recreated replica has an address nginx does not know.
- nginx keeps idle keepalive connections to the api (`keepalive 32`, `nginx.conf:29`). A POST written onto one that a
  draining Kestrel has just closed fails *after* it was sent, and nginx does not retry a POST (no `non_idempotent`). So
  draining a replica that is still in the pool cuts `/api/chat`, MCP and runtime calls.
- Docker's default stop grace is 10 s, while .NET's `ShutdownTimeout` is 30 s, so a drain is killed halfway.
- `RedisRunStateStore` only saves and gets. A run on a killed replica would stay `running` forever, and its page would
  say "Stopping…" forever.

**The restart takes the replica out of rotation before it drains (the pre-stop pattern), one replica at a time:**
**Where the api upstream lives (fifth audit B, sixth audit B2).** `upstream api_pool` moves out of the tracked
`nginx.conf` into a tracked, name-based template, `compose/lb/api.upstream.conf`. Every `make up` copies it to the
generated `compose/lb/conf.d/http/00-api.conf`, and the restart rewrites only that copy. `scripts/docs.py` resolves
upstreams from the template as well as `nginx.conf`, so `make docs-check` passes in CI with no stack and an empty
`conf.d`. nginx refuses a second `upstream api_pool`, so it can live in only one
place, and a generated file is the only place make may rewrite. Every `make up` writes it in its permanent,
name-based form (`server api:8080`). That form self-heals on any reload after a recreation, and dynamic `resolve`
stays dropped (`nginx.conf:11-12`). So even after a SIGKILL mid-restart, the next `make up` heals it.

**With one replica (fifth audit C).** `API_REPLICAS=1` leaves no other replica to take the load, and an empty
`upstream {}` fails `nginx -t`. The out-of-rotation step is then skipped. make announces the downtime and refuses
unless `ALLOW_DOWNTIME=1`. With it, the single replica drains and restarts in place.

1. **Out of rotation** (two or more replicas). For the restart only, make rewrites `00-api.conf` into a transient
   IP-list form, built from the other replicas' container addresses (`docker compose ps -q api` → `docker inspect`),
   and runs `nginx -t && nginx -s reload`. New requests and new keepalive connections now go only to the others.
2. **Drain.** `docker restart -t 40 <container>`. There is no compose primitive for one replica, and the image is
   unchanged, because in-process plugins are compiled in and only the installed set differs. Docker sends SIGTERM, and
   Kestrel finishes the runs in flight. The api's drain is `HostOptions.ShutdownTimeout` = 30 s (.NET's default), and Docker's grace exceeds it: the api
   service's `stop_grace_period` is 40 s, and the restart passes the same `-t 40` (which overrides it). So Docker's SIGKILL never
   races the write that records the leftover runs cancelled. At the end of the 30 s drain, the runs left are recorded
   cancelled.
3. **Healthy.** Wait until the replica is healthy again.
4. **Back in rotation.** For the next replica, the transient list includes this one's new address.
5. Next replica. **At the end**, and in the Ctrl+C/SIGTERM trap, make restores the name-based form and reloads, so the
   lb never keeps a stale IP list. A later `make up`, rebuild or crash restart then heals as it does today.

**Orphaned runs are closed when they are read (fourth audit, optional simplification; fifth audit D).**
- **The owner and its id (sixth audit D1, D2).** `RunState` gains `Instance`, written by the owner
  (`ChatRunFilter.cs:71-73`, `RunStateTracker.Snapshot`). The id is per process start, `Name-Guid`, as
  `TestGenerationHandler.cs:39` already does. `docker restart` keeps the hostname, so `MachineName` alone would let a
  restarted replica's heartbeat vouch for the previous process's runs.
- **The heartbeat.** Each process writes `heartbeat:<Name-Guid>` to Redis every 5 s, with a TTL of 20 s (at least 3×). It
  keeps doing so through the whole drain, until `ApplicationStopped`, so a run still draining is never taken for
  orphaned.
- **Closing on read.** When a run's state is read (by `RunRejoin`, or by the page polling it), and the run is still
  `running` while its owning instance has no heartbeat key, the reader marks it `cancelled`.
- **No overwrite, from either side.** The reader's write is conditional, a compare-and-set in a Lua script, and never
  overwrites a terminal state. It keeps the RunGrace expiry that every write sets today (`RedisRunStateStore.cs:21-22`). So is the owner's own terminal write. Today `RedisRunStateStore.SaveAsync` is a plain
  set; it becomes the same compare. A late owner write cannot undo `cancelled`, and a reader cannot undo a finished
  run.

No page waits forever, and no background sweep is needed.

**Inspector ports.** The two inspectors that share lb's network namespace publish 7172 and 7173 through lb. Those ports
move to a dev-only compose override that is always loaded in dev and qa and never in stage or prod. With the inspector
off, a port simply refuses connections, and lb is never recreated for it.

*Rejected:*
- **Fragments that add to `api`, `lb` and `copilot-runtime`.** They recreate those services on every switch.
- **Compose `include:`.** An included file may not redefine a service the main file has.
- **Profiles alone.** They gate services but not the rest of a plugin.

The `inspectors` profile is retired. Each inspector becomes its own plugin (5o).

### 3. Plugin settings come from the manifests, keyed by plugin

No plugin setting is an indexed environment list. Today's `Agent__Servers__0/1`, the topology service list and the
runtime's hard-coded `['chat','testgen']` are all replaced by data read from the installed manifests, keyed by plugin
name.

- **Api.** A `PluginCatalogue` builds a `Dictionary<string, McpServerOptions>` from each `mcp` plugin's `server.json`
  and allow-list.
- **Runtime.**
  - `chat` stays built in, and any other agent comes from an installed `app` plugin's `[agent]` table: name and path.
  - The runtime still decides nothing (wiring only). It maps names to paths it reads, at start and when the file
    changes.

### 4. nginx: `conf.d`, no dangling upstreams

- `nginx.conf` keeps only the core upstreams and locations, except the api upstream, which is generated into
  `conf.d/http/00-api.conf` (decision 2), plus `include /etc/nginx/lb/conf.d/http/*.conf;` in `http{}`
  and `include /etc/nginx/lb/conf.d/server/*.conf;` in `server{}`.
- `make plugin-on` copies a plugin's `lb.http.conf` and `lb.server.conf` into those directories after its services are healthy, then
  reloads nginx (decision 2).
- A plugin that is not installed leaves no `server mcp-code:8080` line, so nginx always loads. `lb.depends_on` keeps
  only the core services.
- **Absent routes answer 404, not the SPA.** Today a path no location matches falls to `location /` and the web's
  `try_files … /index.html`. So `/code/mcp` with the plugin off would answer 200 with the SPA (review finding 5). The
  core `nginx.conf` therefore reserves the plugin route shapes with a `return 404` for each:
  - `location ~ ^/[a-z0-9-]+/mcp$` for MCP plugins;
  - the A2A agent path shape.

  A plugin's own exact-match location takes precedence over the reservation.

### 5. Compile-time plugins in .NET, enabled by configuration

- `Directory.Build.targets` adds a `ProjectReference` for each `plugins/*/server/*.csproj` to `Maf.Lab.Api` and to the
  test projects.
- At startup the api finds every `IMafPlugin` in those assemblies. For each installed one, in dependency order, it
  applies only the contribution interfaces that plugin implements.
- A disabled plugin registers no service, no route and no table. Its code is in the image but inert.

```csharp
// Identity only. A plugin implements the contribution interfaces for what it actually gives (ISP, see "Principles").
public interface IMafPlugin { PluginManifest Manifest { get; } }

public interface IContributesServices        { void ConfigureServices(IServiceCollection services, IConfiguration configuration); }
public interface IContributesEndpoints       { void MapEndpoints(IMafEndpoints endpoints); }  // routes, admin routes, MapPluginAgent
public interface IContributesModel           { void ConfigureModel(ModelBuilder model); }     // its tables; created only when installed
public interface IContributesDomainBehaviour { IDomainBehaviour Behaviour { get; } }         // Strategy per domain (6)
public interface IContributesTurnObserver    { ITurnObserver Observer { get; } }             // Observer of turns (7)
```

The core asks each plugin what it offers by type (`plugin is IContributesEndpoints e`), so the composition root never
calls a method a plugin does not have, and a reviewer sees a plugin's whole reach from its class declaration.

- `IMafEndpoints.MapPluginAgent(path, AIAgent)` is implemented in `Agent/AGUI/` and calls the same
  `MapAgent(…, AGUIMappings.Agents())`. `AGUIProtocolOnlyTests` stays as strict as it is: a plugin may not reference
  `AGUI.*`, and the one seam is inside the allowed directory.
- A plugin's tables come from `ConfigureModel` into the one `MafDbContext`, so plugin and core rows can share a
  transaction. `DatabaseInitializer`'s create script then includes them only when the plugin is enabled. TestGen's
  backfills move with coverage in its follow-up.

**Kept against review finding 8.** The review proposed today's bare `AddX(services, config)` + `MapX(app)` shape
instead of interfaces. The `IContributes*` interfaces are that same shape, with a contract the composition root can
discover by type, test against (the contract suite) and review at a glance. Without them, every plugin's wiring is a
hand-edited line in `Program.cs`, which is exactly what the core must never name.

Established analogues:
- Orchard Core's `IStartup` (`ConfigureServices` + `Configure`) per module;
- ABP's `AbpModule`;
- VS Code's `contributes` points;
- Backstage's extension points.

Recorded in DECISIONS.md.

*Rejected:* `AssemblyLoadContext` loading of DLLs dropped into a folder. It brings version skew against shared packages
(Agent Framework, MCP SDK, EF), an unreviewed binary in the tenant boundary, and breaks the tests' single build. A
custom plugin that must stay out of the repository should be a **remote** plugin. That is the protocol boundary the
project already trusts.

### 5a. Who writes plugins (decided with the user, 2026-10-05)

In-process plugins come only from this repository, reviewed and covered by the architecture tests. Anyone else writes a
remote plugin: an MCP server or an A2A agent, and an AG-UI agent if it has a screen of its own. A remote plugin speaks
only the official protocols, with no deviation and no customization. That rules out:
- custom methods, headers or events;
- `_meta` keys that the core depends on;
- an SSE or `fetch` path of its own.

The protocol versions are the ones the project already pins (MCP spec revision 2026-07-28, A2A 1.0, AG-UI through
`MapAGUIServer`/CopilotKit).

How a remote plugin is used (its domain, routing questions, guard context and cards) lives in its manifest, which the
operator reviews, and never on the wire. The core reads nothing from the remote side beyond what the protocol defines:
tool lists, tool results, agent cards and tasks. The manifest is reviewed configuration, not a protocol extension. The
routing questions also stay out of the remote server's hands, because a third party must not shape a Jev decision.

### 5d. The monitor is a dev tool, never in stage or prod (decided with the user, 2026-10-05)

`monitor` is an installation-scoped plugin. Tenants never see it. It is meant to be installed on dev and qa and left out
of stage and prod.

**Environments.** Every manifest declares where the plugin may be installed: `environments = ["dev", "qa"]` for dev
tools, and all environments by default. `MAF_ENV` (`dev` | `qa` | `stage` | `prod`, default `dev`) names the
deployment.

**Enforcement.** make and the api both refuse a plugin whose `environments` does not include `MAF_ENV`. make fails
before starting anything. The api fails at startup, naming the plugin, so a stray environment variable cannot bring the
monitor into prod.

Dev-only plugins under this rule:
- `monitor`
- `a2a-inspector`, `mcp-inspector`, `redis-insight`, `neo4j-browser`
- `evals`
- `curriculum`

**What prod keeps without the monitor:**
- the core's own turn record (envelope, guard signals, intent and domain), which the guard and the answer check need;
- OpenTelemetry spans and metrics, which carry no message content.

The full prompt, the tool schemas, raw model messages and AG-UI frames are therefore never recorded in stage or prod.

### 5e. Dev and qa plugins are absent from stage and prod images (decided with the user, 2026-10-05)

A plugin whose `environments` excludes `stage` and `prod` is not merely inactive there: its code is not in the images.

**Two image variants.** CI builds both from one commit:

- **`full`** holds every plugin and is the image for dev and qa.
- **`product`** holds only the plugins allowed in stage or prod. It is built like this:
  - `Directory.Build.targets` skips the other plugins' `server/` projects when `MafImageVariant=product`;
  - the web's `import.meta.glob` list is generated from the allowed manifests;
  - the compose fragments and lb snippets of those plugins are not shipped.

**Promotion.** qa runs the `product` variant as well as `full`. stage and prod promote the exact `product` image qa
tested, never a rebuild.

**Proof of absence.** A CI check on `product` lists its assemblies and web chunks and fails if any belongs to a
dev-or-qa-only plugin. The api's startup refusal (5d) stays as a second guard.

### 5f. Tools reach the agent only through MCP (decided with the user, 2026-10-05, a hard rule)

Every tool the chat agent is offered comes from an MCP server, whether the core's billing server or a plugin's. No
plugin, in-process or remote, registers an `AIFunction`/`AITool` with the agent, and no seam for that exists. Every tool
therefore has one path:
- the tenant comes from the token;
- the operator-reviewed allow-list comes from the manifest;
- the call is audited in `ToolAudit`;
- a write is confirmed through MRTR `input_required` at the MCP layer;
- a stop is MCP's own cancellation.

**Enforcement.** An architecture test fails a plugin assembly that calls `AIFunctionFactory` or implements
`AITool`/`AIFunction`.

**Overhead.** If one container per small tool becomes heavy, the answer is a shared MCP host, a bundled plugin that
serves several small plugins' tools from one MCP server, each behind its own path and audience. It is never a second
path for tools.

### 5g. Every domain is a plugin, billing included (decided with the user, 2026-10-05)

The core is a domain-agnostic assistant shell. Billing is this lab's first domain, not the product's. A real deployment
may start with a completely different domain. So billing and portfolio are tenant-scoped plugins like any other, and no
domain is privileged.

**What stops being core:**
- **The billing-first assumptions:**
  - `AgentOptions.McpEndpoint` as "the first server";
  - "every unmapped tool belongs to billing" (`Domains.OfTool`);
  - "an unreachable billing server fails the turn";
  - `Domains.Billing`, which nine source files use.

  A turn now needs at least one domain the tenant has enabled, and an unknown tool belongs to no domain.
- **The domain wording in the system prompt.** `Prompts/system.v5.md` mentions billing 29 times. It splits into a
  generic core prompt (citations, refusals, injection defense, confirmation of writes) and a prompt fragment per domain.
  Each fragment lives in its plugin folder (`prompt.md`) and is reviewed with it. The core assembles the prompt from the
  tenant's enabled domains. A remote plugin's fragment is likewise operator-reviewed configuration, never read from its
  server.
- **Guard and answer-check contexts,** which come from each domain's `guard_context`.
- **Fee adjustment.** It moves into the `billing` plugin. Its write tool is offered only when `compliance` is enabled
  for the tenant.
- **The billing corpus, collection, graph templates and eval datasets.** They move into the `billing` plugin:
  `data/`, the billing graph build, the `selection` suite's billing cases.

**Stores are infra plugins that domains depend on.** The shared retrieval library stays core code. The data stores it
talks to are infra plugins:
- `qdrant`
- `neo4j`

Embeddings are a provider (`introduce-provider-plugins`), not an infra plugin. Until then the two Ollama instances stay
as they are, and the embedding path to `ollama-batch` with its `num_thread` is unchanged (`ModelProviders.cs:105-118`).

A domain plugin requires the stores it needs through its manifest `depends`. A deployment whose domains need no graph
runs no Neo4j. Redis stays core, because run state and stops live there.

**Order.** This change removes the billing-first assumptions from the contract and the core. Moving the `billing`
folder itself is the first follow-up, before any other. Until then the billing plugin's manifest points at the existing
`mcp-retrieval` service.

### 5h. No domain in use: the assistant declines (decided with the user, 2026-10-05)

A tenant with no domain plugin in use gets no answer from a model. The turn ends before any model or decision-engine
call, with a fixed message: no domain is enabled, ask the administrator. The message is shown in the chat's own
design, and the turn is recorded with no tool call.

The chat page says the same before the first message, so nobody types into an assistant that cannot answer. A tenant that
wants open conversation without sources gets it only from a deliberately allowed and enabled plugin (for example
`general-chat`), never from the core.

### 5x. What `make core` runs (review finding 1)

`make core` starts the core services and no domain, app or dev plugin. Today's decision engine (Jev) and chat model
stay wired as they are now. Making them provider plugins, with `MAF_CORE_PROVIDERS` naming the core's minimum, is
`introduce-provider-plugins`. The core-only CI leg (task 7.1) asserts that the stack starts healthy and declines every
turn (5h): no model, decision-engine or tool call is made.

### 5y. The conversation list is a plugin; memory stays core (decided with the user, 2026-10-06; fixed by the twelfth audit)

**The core keeps the conversation itself:**
- `POST /api/conversations`;
- `GET /api/conversations/{id}` and `/{id}/pending` (reopening, and confirmations in flight);
- `useConversation` and `useUserKey`;
- `SqliteChatHistoryProvider`;
- the stores that `data-lifecycle` covers;
- **"New conversation"** in the chat's header. The header already has it (`ChatPage.tsx:305-307`), so the core keeps
  that button. The sidebar's duplicate is dropped.

**A core seam for the store.** `IConversationStore`, in `Maf.Lab.Plugins.Abstractions`, implemented by the core over
`MafDbContext`. It offers:
- page and search of the caller's own conversations;
- rename;
- a soft delete that records `conversation.delete` in the audit, as `HistoryEndpoints` does today.

The plugin reaches storage, titles (`ConversationTitles`) and the audit (`ToolAudit`, `AuditKinds`) only through it,
never through `Maf.Lab.Api` types, which it cannot reference (decision 5).

**The screen is the plugin `conversation-history`** (app, **installation scope**, every environment, installed by
default when `MAF_PLUGINS` is unset). Whether a deployment shows a conversation list is a deployment choice (a kiosk or
an embedded widget leaves it out), so it is not a per-tenant switch. It takes:
- the list, search and relative times, as a `chatSidebars` registry entry. The entry owns its drawer and collapse state,
  its "☰ History" toggle, its backdrop and its collapsed rail (today `ChatPage.tsx:67-68,272-303`). The core renders the
  toggle only when the registry is non-empty, so with the plugin off there is no sidebar chrome at all;
- the list route `GET /api/conversations`, rename `PATCH /{id}` and a user's delete `DELETE /{id}`, over
  `IConversationStore`;
- the refresh of its own list after a run. Today `ChatPage.tsx:181` invalidates `['conversations']`, a hidden
  core→plugin coupling. It moves to the plugin's `runObservers.onRunEnd`.

**Tests move or change seam.** Core tests that use the moved routes as fixtures are moved to
`plugins/conversation-history/tests/`, or rewritten on `IConversationStore`:
- `ComplianceApiTests` (:84-85, 106, 127, 159, 184);
- `MessageRetentionTests` (:69);
- `HandleTests` (:30, 37);
- `ChatHistoryTests` (list, search and paging at :82-98, :170, :196, :222; rename and delete at :184-194). These move to
  the plugin. Its title, open and continue parts stay core, on `GET /{id}`;
- `ChatPage.stop.test.tsx` (:224);
- `ChatPage.history.test.tsx` (:122, the sidebar assertions), which moves. Its scenario "New conversation while a stored
  one is open" (:234-258) stays core and clicks the header button instead.

The sidebar's own "＋ New conversation" (`HistorySidebar.tsx:52,79`) is dropped. There is one "New conversation", in
the header, and a new core web test exercises it.

So deleting the plugin's folder keeps `make test` green.

**The main spec.** `chat-history` is MODIFIED, not added around. The list, rename and delete apply while the plugin is
installed, and "New conversation" and reopening by URL are unconditional.

Deleting a user's or a tenant's data stays the core's job (`data-lifecycle`), whatever the screen.

### 5i. Insights are a dev tool (decided with the user, 2026-10-05)

`insights` is installation-scoped and allowed only in dev and qa. It holds:
- Jev and intent statistics.

Tenants never see it, and it is not in the `product` image. The feedback review screen was here at first and moved to its
own plugin (5j). The statistics keep reading the core turn record, so the
plugin needs nothing from `monitor`.

### 5j. Feedback stays a product feature (decided with the user, 2026-10-05)

The answer feedback buttons under every answer stay core and work in every environment:
- Wrong tool;
- Wrong document;
- Wrong answer;
- Wrong confirmation summary, on a write's confirmation.

They post to `POST /api/feedback`. These four buttons are the assistant's only rating; there is no thumbs up/down.

Reading the feedback is the `feedback-review` plugin, which is tenant-scoped and allowed in every environment. A tenant's
`TENANT_ADMIN` sees and labels the tenant's own rated turns, as on `/admin/feedback` today. The screen shows each rated turn
from the core turn record: question, answer, sources, chosen domain and the feedback. The full stored trace
(`StoredTracePanel`) is added through a monitor tab only where `monitor` is installed, so in stage and prod it simply
isn't there.

Labels are stored wherever the plugin runs. Turning labelled turns into eval datasets (`DatasetWriter`, which writes
under `evals/`) belongs to `evals`, so it exists only in dev and qa. Bringing labelled prod turns into dev datasets
would carry message content across environments. That is a separate decision, with its own retention rule, and this
change does not make it.

### 5k. No cap on domains per tenant (decided with the user, 2026-10-05)

A tenant may use any number of domains. Every routing question of every domain in use goes into the turn's one
decision request, which answers many questions in one call (jev-usage.md: all questions over one state in ONE request).
There is no pre-filter in front of it and no limit in the manifest or the operator screen.

**Cost.** Each extra domain adds its routing questions and their answer options to that request's input tokens. Jev
bills input tokens, so cost per turn grows with the number of domains in use. The decision engine's metric records
input tokens per turn by domain count, so the growth is measured rather than guessed. This matters while the TypeSafe
billing discrepancy is open (charged $0.41 against $0.0335 estimated).

The manifest carries `schema = 1` so its format can evolve. No other API version is needed, because every in-process
plugin is compiled with the core in this repository (5a).

### 5o. Each inspector is its own dev/qa plugin (decided with the user, 2026-10-05)

`a2a-inspector`, `mcp-inspector`, `redis-insight` and `neo4j-browser` are four separate plugins. Each is installation-scoped and
allowed only in dev and qa, so all four are absent from the `product` images. Each one carries:
- its compose service;
- its port. The a2a and MCP inspectors share lb's network namespace, and their ports 7172 and 7173 are published by lb
  from the dev-only compose override (decision 2), never added by the plugin's fragment, so lb is never recreated;
- its nav link, a web `nav` contribution with `external: true`. The hard-coded `INSPECTORS` list in `Layout.tsx`
  goes away.

**Dependencies:**
- `a2a-inspector` depends on `a2a`, since there is nothing to inspect without the A2A surface;
- `mcp-inspector` takes the MCP endpoints it offers from the installed plugins' manifests instead of the three paths
  hard-coded in `compose/mcp-inspector/start.mjs`;
- `redis-insight` needs only core Redis;
- `neo4j-browser` depends on `neo4j` and gains a nav link like the other three. It stays on loopback (7175,
  DECISIONS §75), never on `neo4j` itself.

### 6. Domains become data: `IDomainDescriptor`

`Domains` (static) is replaced by an `IDomainCatalogue` built from the core billing descriptor and every enabled
plugin's `[domain]` table:

```csharp
public sealed record DomainDescriptor(
    string Id, string? SearchTool, IReadOnlyList<string> GraphTools,
    IReadOnlyList<string> RoutingQuestions, string? GuardContext,
    IReadOnlyDictionary<string, string> CardTypes);   // tool → activityType
```

What changes:
- **`ChatTurnRunner`:** its codebase branches become "the turn's domain has a search tool" and "the turn's domain has
  graph tools".
- **Jev routers:**
  - `DataToolRouter` and `CodeToolRouter` build their closed answer spaces from the catalogue. It is still one request
    over one state (jev-usage.md); a disabled domain is simply not an option.
  - Routing questions are fixed per deployment, never taken from a request or the model, so the rule "closed answer
    spaces only" holds.
- **`Domains.OfTool`:** reads the catalogue; an unknown tool is no longer silently billing.
- **Guard and answer check:** read `GuardContext` instead of the codebase constant.
- **Portfolio:**
  - Its focus logic (`PortfolioTools`, `ResultSummary`) is a domain behaviour, not just data. Rather than become a
    descriptor field, it moves behind `IDomainBehaviour`, which an in-process plugin may register next to its
    descriptor.
  - Portfolio is therefore an `mcp` plugin with a small `server/` part.

This is the one refactor inside the chat path, and it lands with tests that pin today's routing for billing, portfolio
and codebase before and after.

### 7. The trace splits into a core record and the monitor's observer

- **Core.** `TurnTrace` stays and keeps only what the core reads: envelope, guard signals, intent and domain events, and
  run identity.
- **Observer seam.** Everything else is published to `ITurnObserver` (`OnEvent`, `OnModelCall`, `OnPrompt`,
  `OnFrames`). The core registers none.
- **Monitor plugin.** The `monitor` plugin registers the observer and takes over everything else:
  - **What it records:** `LiveTrace`, the full message capture from `TracingChatClient`, the prompt and tool-schema
    dump, `TraceRetrieval` diagnostics, and `RunFrameRecorder`.
  - **Where it keeps it:** its own `TurnDiagnostics` table, keyed by turn.
  - **Routes:** `/api/runs/{id}/trace` and `/api/turns/{id}/trace`.
  - **Retention:** its own `TraceRetentionService`.
- **Why this matters.** With the monitor off, a turn does no per-event Redis write, no full-message serialization, and
  no 1 MB row.
- **Reasoning on reload** moves into the core turn record as its own field, so it no longer needs the monitor
  (`ChatPage.tsx:220`).
- **Statistics.** Intent and Jev statistics read the core record, which still carries intent and domain events.
- **Prompt in the trace.** Showing the system prompt in the trace stays a decision of the monitor (memory: normal turns
  keep the full system prompt), not of the core.

### 8. Web: registries and a runtime-enabled set

- Every plugin's `web/index.ts` is found at build time with `import.meta.glob('/plugins/*/web/index.ts')`. For that:
  - the web image's build context moves to the repository root, with a `.dockerignore`;
  - Vite's `server.fs.allow` covers `plugins/`;
  - `tsconfig` includes it.
- At start-up the web asks `GET /api/plugins` (the names in use, plus each one's domain and card ids). It then calls
  `register` only for those, so switching a plugin needs no web rebuild.
  - **Before sign-in** the route answers anonymously with only the plugins whose manifest says `public = true`. Only
    `dev-login` does, and it is absent from stage and prod, so in prod the anonymous answer is empty (re-review).
  - **After sign-in** the web asks again and registers the rest.
- Each plugin is wrapped in its own `ErrorBoundary`.

```ts
definePlugin({
  name: 'code',
  routes:      [{ path: 'code', element: <CodePage/>, admin: false }],
  nav:         [{ to: '/code', label: 'Code' }],
  chatPanes:   [{ id: 'code', label: 'Code snippets', badge: turn => …, render: ctx => <CodeSnippetsPanel {...ctx}/> }],
  chatSidebars:[],                               // left of the chat; conversation-history fills it (5y)
  sourceActions: [{ kind: 'code', onOpen: (source, ctx) => ctx.openPane('code', source) }],
  cards:       { 'maf-lab/holdings': HoldingsCard },
  monitorTabs: [],                               // the monitor plugin owns this registry and exposes it
  runObservers:[],                               // onRunStart / onEvent / onRunEnd — how the monitor sees frames
});
```

What moves where:
- **Core chat.** It renders `chatPanes` in the aside, which is absent when none are registered. Bubbles render
  `turnActions` and an optional `turnView` override, which is how time travel shows a rewound turn. `chatReducer` loses
  `traces`.
- **Monitor.** It keeps its store keyed by turn in its own context. It starts live polling only from its run observer,
  and only while its pane is open.
- **Shared code.** `formatDate`, `Page.module.css`, `StopHint`, `useEscToStop` and `ErrorBoundary` move into a core
  `web/src/shared/`. `api/types.ts` sheds plugin types into their plugins.
- **The plugin API.** It is a small, typed `web/src/plugins/api.ts`, and the core never imports a plugin. ESLint enforces core→plugins (`no-restricted-imports`), and an architecture test (a Vitest scanner) enforces
  plugins→core, because ESLint's flat config cannot see files outside `web/`. The directions are:
  - core → `/plugins/**` is forbidden;
  - plugin → core internals other than `shared/` and `plugins/api` is forbidden.

### 9. Checks

All of these run in `make test`, `make docs-check` and CI.

- **Existing fitness functions widen to plugin code** (review finding 7). Today `AGUIProtocolOnlyTests` and the tenant
  query-path tests enumerate `src/` only (`AGUIProtocolOnlyTests.cs:53`). Their roots become `src/` plus
  `plugins/*/server/`, so plugin code cannot escape them.
- **Plugin tests travel with the plugin.** Today's tests of a moved feature (for `code`: `CodeToolRoutingTests`,
  `CodebaseSearch*` and the others) move to `plugins/<name>/tests/`, and the test projects glob them in. This is how
  "delete the folder, `make test` passes" holds.
- **Architecture (xUnit).** A test fails when any of these holds:
  - core assemblies reference a `Maf.Lab.Plugins.*` assembly;
  - a plugin assembly references `Qdrant.Client`, `Neo4j.Driver` or `AGUI.*` types;
  - a plugin's endpoint takes a parameter named or typed as a tenant (same rule as core).
- **Boot matrix.** `ApiFactory` takes the enabled set:
  - with no plugin (5x), the api boots, a turn declines, and no plugin route answers;
  - with all plugins on it boots too, and the existing suite runs there unchanged.
- **Web.** Vitest renders the app with no plugins (no aside, nav = Chat only) and with all of them.
- **Manifests.** `docs.py` validates each `plugin.toml`:
  - required keys;
  - `progress` and `stopping` in the forms the proposal checks already accept (`Terminal:`/`Page:`/`None — reason`;
    `Key:`/…/`None — reason`);
  - `depends` resolve.
- **Docs.**
  - `docs.py` globs `plugins/*/plugin.mk` for make targets and `plugins/*/lb.http.conf` and `plugins/*/lb.server.conf` for routes. Each route row names its
    plugin.
  - `ROUTE_SOURCES` includes `plugins/*/server`.
  - Documentation describes every plugin present, whether enabled or not.
- **CI.**
  - The e2e job runs with every plugin (unchanged behaviour).
  - A new core-only leg runs `make core`, `verify` (core checks only; `verify_lb.sh` and `conformance.mjs` skip a
    plugin's checks when `/api/plugins` does not list it) and one chat turn against the CI stub.

### 10. Catalogue of bundled plugins

Scope is `tenant` or `installation` (per-tenant use arrives with `enable-plugins-per-tenant`). The Environments column says where a plugin may be installed (5d, 5e): "all",
or "dev, qa" for a plugin that is absent from stage and prod images.

| Plugin | Kind | Scope | Environments | Takes with it |
|---|---|---|---|---|
| `billing` | mcp | tenant | all | mcp-retrieval, the billing corpus, collection and graph templates, its prompt fragment, fee adjustment, the billing eval cases; depends on `qdrant`, `neo4j`, `ollama-embeddings` |
| `portfolio` | mcp | tenant | all | mcp-portfolio, its collection, household cards, portfolio domain behaviour (`IDomainBehaviour`) |
| `compliance` | a2a | tenant | all | compliance agent, `/compliance` route, audit screen and routes |
| `a2a` | app | tenant | all | the assistant's A2A surface for partners, A2A admin screen |
| `conversation-history` | app | installation | all | the conversation sidebar (list, search, rename, delete) and its three routes over `IConversationStore`; installed by default (5y) |
| `feedback-review` | app | tenant | all | the Feedback review screen for the tenant's admin, its queue and label routes; shows the core turn record (5j) |
| `insights` | app | installation | dev, qa | Jev and intent statistics (5i) |
| `code` | mcp | installation | dev, qa | mcp-code, code collection and code graph, code snippets pane, `/api/code/snippets`, codebase domain |
| `monitor` | app | installation | dev, qa | Behind the scenes pane and its seven tabs, live trace, frame recording, trace routes, trace retention |
| `coverage` | app | installation | dev, qa | coverage-runner, test-agent, testgen AG-UI agent, coverage page, A2A test-agent admin, its tables |
| `evals` | app | installation | dev, qa | eval reports screen and routes, `make eval*`, `DatasetWriter`, the A2A probe |
| `topology` | app | installation | dev, qa | topology screen and probe |
| `curriculum` | app | installation | dev, qa | curriculum screen |
| `a2a-inspector` | infra | installation | dev, qa | the A2A Inspector on 7172, its nav link; depends on `a2a` |
| `mcp-inspector` | infra | installation | dev, qa | the MCP Inspector on 7173, its nav link; its server list comes from the installed MCP plugins |
| `redis-insight` | infra | installation | dev, qa | Redis Insight on 7174, its nav link |
| `neo4j-browser` | infra | installation | dev, qa | Neo4j Browser on 127.0.0.1:7175, its nav link; depends on `neo4j` |
| `index-admin` | app | installation | all | index admin screen and routes, admin jobs, the Api → Indexing reference |
| `observability` | app | installation | all | otel-collector, prometheus, jaeger, telemetry screen; without it services export nothing (as today) |
| `qdrant`, `neo4j` | infra | installation | all | the stores; required by domain plugins through `depends` |
| `_example` | mcp | tenant | dev, qa | a 40-line MCP server in a container, with a domain descriptor; the authoring template, off by default, green in CI |

The `code` plugin is a domain, but its subject is this repository, so it is a dev tool for the whole installation rather
than something a tenant enables.

**Fee adjustment and compliance.**
- Fee adjustment is a billing write, so it goes with the `billing` plugin (5g).
- Its tool is offered only when `compliance` is in use. Today it is offered without a reviewer, and then
  its review can never pass.

**Dev sign-in.** `DevTokenPicker` and `/dev/*` become the `dev-login` plugin (installation, dev and qa) once the
company IdP is wired in (`adopt-company-idp`). Until then they are the only way in and stay where they are.

### Decisions taken during implementation (part A, 2026-10-06, decided by the reviewing session)

These choices deviate from the text above. Each is kept, with the rejected alternative recorded in DECISIONS §81:

1. **`IMafPlugin` carries only `Name`.** The manifest is resolved from `plugins/.installed` by name, so it has a single
   source in `plugin.toml`. The join fails closed both ways:
   - an installed server part with no code stops the start;
   - a compiled plugin with no manifest fails the contract suite;
   - duplicate names are refused.

   Rejected: a `PluginManifest` property duplicating the toml in code.
2. **copilot-runtime re-reads `.installed` on read when its inode or mtime changes.** There is no watcher and no Redis
   client in a wiring-only service. The api keeps Redis `plugins-changed` plus the 30 s re-read. Rejected: a
   FileSystemWatcher (unreliable over bind mounts), and a Redis client in the runtime.
3. **The plugins→core web boundary is an architecture test**, a Vitest scanner that fails when plugin web folders exist
   but yield no files. ESLint covers core→plugins. Rejected: dependency-cruiser (a second tool and a new devDependency
   for one rule).
4. **A plugin's nginx parts are two files, `lb.http.conf` and `lb.server.conf`.** One file cannot be included in both
   `http{}` and `server{}`. This corrects the earlier single `lb.conf`.
5. **The manifest validator is a stdlib subset of JSON Schema** (draft 2020-12, declared in `$schema`). It refuses
   unknown keywords, and a test asserts the schema uses only supported ones. Rejected: `jsonschema`, which would be
   the first third-party Python package.

**Corrected back to the design:** qa defaults to the `product` image, as stage and prod do (5e).

## Principles and patterns

Every plugin, and every change to the core's seams, is reviewed against this section. A plugin that needs to break it
needs a design decision first.

**Architecture.**
- **Microkernel / plug-in architecture** (Buschmann, *POSA*). A small core defines extension points and plugins fill
  them. The core never knows a concrete plugin; a plugin knows only the core's contracts. Backstage, VS Code
  (`contributes`) and Orchard Core work the same way.
- **Ports and adapters.** The seams are the ports. MCP, A2A and AG-UI, in their official form only, are the adapters
  to the outside (5a, 5f).
- **One composition root** (Seemann). Only `Program.cs` assembles the system: core first, then plugins in dependency
  order. Nothing else news up a plugin or reads `MAF_PLUGINS`.

**Lifecycle.**

| Phase | What happens |
|---|---|
| Build | Discovery through the MSBuild glob and `import.meta.glob`; the `product` variant leaves out dev and qa plugins (5e). |
| Startup | Manifests are loaded and validated. The environment check fails fast with the plugin's name. Plugins are sorted topologically, and a cycle is an error. Contributions are applied, and each plugin's routes are mapped in its own route group behind the core's gate filter. |
| Request | Principal → tenant → `PluginSet` snapshot for the turn → domain catalogue → Jev questions, prompt and tools → observers. |
| Change | One atomic write, published on Redis; every replica drops its cache; the next turn sees the new set. |

**SOLID.**

| Principle | How it holds |
|---|---|
| Single responsibility | A plugin is one capability in one folder. The core orchestrates: it does not search, render dashboards or know a domain. |
| Open/closed | A new domain or tool is a new folder with no core change. The "delete the folder, everything stays green" test proves it. |
| Liskov substitution | Every `DomainDescriptor` is used the same way, and no domain is a special case (5g). |
| Interface segregation | `IMafPlugin` is identity only, and each `IContributes*` is one small capability. On the web, each `definePlugin` field is its own small type and every field is optional. |
| Dependency inversion | The core depends on `IDomainCatalogue`, `ITurnObserver` and the registries, never on `Maf.Lab.Plugins.*`. An architecture test enforces it. |

**Patterns in use.**
- Strategy: `IDomainBehaviour` per domain.
- Observer: `ITurnObserver`, and the web's run observers.
- Decorator: `TracingChatClient` around the model client, registered only by `monitor`.
- Registry / extension point: the web registries, `tenantAdminSections` and `platformAdminSections`.
- Chain of responsibility: the core's gate filter in front of every plugin route group.

**Practices.**
- **Fitness functions** (*Building Evolutionary Architectures*). The rules are tests, not prose:
  - the core references no plugin;
  - no plugin touches Qdrant, Neo4j, AG-UI or `AIFunction`;
  - nothing takes a tenant parameter;
  - the `product` image holds no dev code;
  - the web lint enforces the import boundary both ways.
- **Contract tests.** One shared suite runs against every plugin:
  - its manifest;
  - booting with and without it;
  - its `progress` and `stopping`;
  - its routes behind the gate;
  - deleting its folder.
- **Configuration** (12-factor). The environment and the plugin set come from the environment. Build once, promote the
  same artefact.
- **Security:**
  - Least privilege and defense in depth: a gate in the core in front of every plugin route. The later changes add
    allow and enable per tenant, an audience check at every server and break-glass for content.
- **Failure and change:**
  - Fail fast: a wrong environment, a missing dependency or a cycle stops startup or make, naming the plugin.
  - Idempotent, atomic state changes: a toggle is one conditional write, safe to repeat, taken by any replica.
- **Decisions:**
  - Decision records: every architectural choice lands in DECISIONS.md.
  - Strangler fig migration: plugins leave the core one at a time, and "everything installed" equals today's
    behaviour at every step.

## Risks / Trade-offs

- **`IDomainDescriptor` touches the chat path and Jev.**
  - **Risk:** routing could change.
  - **Mitigation:** pin today's decisions for billing, portfolio and codebase in tests first, and run
    `make eval SUITE=selection` before and after (Jev rule: evals on change of tool set). The question sets are built
    from fixed manifests, not requests.
- **Moving the web build context to the repository root.**
  - **Risk:** a larger context.
  - **Mitigation:** a `.dockerignore` that admits only `web/`, `plugins/*/web/` and `plugins/*/plugin.toml`.
- **Compose `-f` merging.**
  - **Risk:** it is order-dependent for lists. `ports` and `volumes` append, and environment merges by key.
  - **Mitigation:** fragments only add, never override a core value, and a test runs `docker compose config` for
    `none`, `all` and each single plugin and checks that the core services are identical.
- **A plugin that is on but whose service is down.**
  - **Behaviour:** the core already degrades for MCP and A2A.
  - **Shown:** `/api/plugins` reports `enabled` and `healthy` separately, so the nav greys the plugin out instead of
    hiding it.
- **Two ways to add a tool (remote or in-process).**
  - **Risk:** authors may not know which to choose.
  - **Mitigation:** the guide says to start remote, and to go in-process only for a screen or an api route.
- **Follow-up moves are long.**
  - **Mitigation:** each is a code move behind seams that already exist after this change. Each must leave
    `make core` and the all-plugins suite green.

## Migration Plan

1. Land the seams with the current behaviour (all plugins on means today's stack). Keyed settings ship with a
   compatibility binding for the indexed form for one change.
2. Move the four inspectors, then `code`, then `monitor`. The suite runs after each.
3. Follow-ups move one plugin each, `billing` first (with the store plugins it depends on), then `portfolio`,
   `compliance`, `a2a`, `coverage`, `evals`, `insights`, `feedback-review`, `index-admin`, `observability`, `topology`, `curriculum`.
4. When the last one lands, the compatibility binding and `Program.cs`'s lab registrations are gone. Program.cs then
   registers only the core and calls `AddMafPlugins()`, which `EvalAgentHost` reuses instead of recomposing the DI.

**Rollback** is per step. `MAF_PLUGINS` unset is the full lab at every point.

## Open Questions

None open in this change. The decisions taken with the user are numbered 5a–5w and 5y; 5x comes from the review.
Those that moved to a later change keep their letter there:
- `rename-firm-to-tenant`: 5q.
- `adopt-company-idp`: 5r, 5s, 5n.
- `enable-plugins-per-tenant`: 5b, 5c, 5l, 5m, 5p.
- `document-acls`: 5v.
- `data-lifecycle`: 5w.
