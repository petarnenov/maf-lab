# Plugins

The core of maf-lab is a domain-agnostic assistant shell. Everything optional is a plugin: a domain the assistant
answers about, an agent it consults, a screen, a provider, a dev tool. A plugin is **one folder**; deleting the folder
removes it from every part of the system, and `make test` and `make docs-check` stay green. Design:
`openspec/changes/introduce-plugins/design.md` (decisions 1-10, "Principles and patterns"); spec: `plugins`.

## Start one

```bash
make plugin-new NAME=weather KIND=mcp   # a copy of _example under the new name: an MCP server in its own container
make plugin-new NAME=notes KIND=app     # an in-process plugin: one route over a core port, a page, their tests
make docs                               # the plugin's routes, targets and catalogue row reach the generated docs
make plugin-on NAME=weather             # start it on the running stack
```

`plugin-new` prints the files it wrote. It refuses an existing folder, a name that is not a plugin name (lower-case
letters, digits and hyphens, starting with a letter), a leading underscore and an unknown kind, each with one line
saying what to change. It writes into a temporary folder and renames it into place last, so a stop part-way leaves
nothing behind. `make plugin-new-check` (in CI) proves a fresh plugin of each kind builds and keeps the docs in sync.

`_example` is the authoring template: the smallest MCP plugin, a C# server on the official MCP SDK in its own
container, with one tool (`get_example_fact`) and a domain descriptor. It is off unless asked for (a name with a
leading underscore is never installed by `MAF_PLUGINS` unset); CI installs it, and `make verify` asks it one routed
question while it is in use.

## A plugin folder

```
plugins/<name>/
  plugin.toml          # the manifest (required): checked against plugins/plugin.schema.json
  server.json          # an MCP plugin's server, in the official MCP Registry format
  compose.yml          # its own services only; never adds to api, lb or copilot-runtime
  compose.ci.yml       # its CI override (CI_MODE=1), merged after compose.yml
  lb.http.conf         # its nginx upstreams (included in http{})
  lb.server.conf       # its nginx locations, exact matches (included in server{})
  plugin.mk            # its make targets, joined to the core's (`index: index-<name>`, `verify: verify-<name>`)
  prompt.md            # a domain's prompt fragment (summary, scope, tools, examples, rules sections)
  service/             # a remote plugin's own server, built into its own image (any language)
  server/              # Maf.Lab.Plugins.<Name>.csproj: code that runs in the api (in-process plugins only)
  web/index.ts         # definePlugin({...}): code that runs in the web app (in-process plugins only)
  docs/http-api.md     # its api routes, in the table shape of docs/http-api.md, which transcludes it
  tests/unit/          # compiled into Maf.Lab.Tests;      tests/integration/ into Maf.Lab.IntegrationTests
  files/               # what its containers mount
```

Only `plugin.toml` is required. Paths in a plugin's compose files resolve against `compose/`, the project directory,
as Compose does for every merged file: `../plugins/<name>/files/…`. A plugin's tests use the core's test support
(`ApiFactory` in C#, `@maf/testing` on the web) and leave with the folder; give the plugin's own server under
`service/` no `tests/unit` files, because those compile into the api's test host.

Two kinds of plugin cover almost everything:

- **Remote plugins** (`mcp`, `a2a`) add a capability over a protocol the project already speaks: an MCP server or an
  A2A agent, in any language. They need no C# in the api and no React. Start here.
- **In-process plugins** (`app`) add api routes (`server/`) or screens (`web/`). Use one only for a screen or a route.

A `provider` plugin implements something the core needs exactly one or more of (a decision engine, a chat model,
embeddings); an `infra` plugin is a store or a dev tool.

## The manifest

```toml
schema       = 1
name         = "weather"                # the folder's name; a leading underscore marks an opt-in bundled plugin
kind         = "mcp"                    # mcp | a2a | app | provider | infra
scope        = "tenant"                 # tenant (a tenant may choose it) | installation (the whole deployment)
environments = ["dev", "qa", "stage", "prod"]
description  = "Forecasts for the account's region"
depends      = []                       # other plugins it needs; installed first
# public     = true                     # listed by /api/plugins before sign-in (only a sign-in plugin needs this)
progress     = "None — every call is short"
stopping     = "None — no long work; a call stops with its MCP request"

[domain]                                # an MCP plugin: how the core uses its tools (operator-reviewed data)
id            = "weather"
order         = 90                      # where the domain sits among the others
question_key  = "in_weather"            # its routing question's key (default in_<id>)
description   = "Questions about the weather in an account's region. Not in it: billing, fees or portfolios."
tools         = ["forecast"]            # the allow-list; the endpoint comes from server.json
search_tool   = "forecast"              # called with {"query": question} on a procedural turn (forced retrieval)
guard_context = "documents"             # the content guard's context for what the tools return
prompt        = "prompt.md"             # the prompt fragment

[domain.scope_summary]                  # the domain as the out-of-scope reply names it, by language
en = "questions about the weather"
bg = "с въпроси за времето"

[agent]                                 # an app plugin that serves an AG-UI agent
name = "weather-agent"
path = "/api/weather/agent"

[topology]                              # where the topology screen probes it
url   = "http://weather:8080/health"
label = "weather"
```

A domain may also name `graph_tools`, `read_tools` and `write_tools` (a data question's routing, with their
descriptions), `card_types` (a tool's result as an AG-UI activity card), `tool_requires` (a tool offered only while
another plugin is in use) and `search_any_intent`. `plugins/plugin.schema.json` is the full reference.

`progress` and `stopping` take the forms every proposal takes (`progress-feedback`, `stop-anything`): `None — <reason>`,
`Terminal: …` and/or `Page: …`; for stopping, `Key: …; Stop: …; Recorded in: …; Shown: …`. `make docs-check` fails a
manifest without them, with an unknown key, or with a dependency that does not exist.

A remote plugin's server is described by its own standard document, never restated in the manifest: `server.json`
(MCP Registry) for an MCP server, the Agent Card for an A2A agent. Its routing questions, guard context and card types
live in the manifest, which the operator reviews, and are never read from the remote side, so a third party cannot
shape a decision or the system prompt.

A remote plugin's server is reached through the balancer (`lb.server.conf`, an exact `location`) with the caller's own
bearer token. It validates that token itself (the platform's `Auth` settings, from `compose/env/platform.env`) and
takes the tenant from its claim, never from a tool argument; `_example`'s `service/Program.cs` shows the whole of it.

`scope = "tenant"` is recorded today, but an installed tenant-scoped plugin is in use for every tenant: per-tenant
allow and enable arrive with `enable-plugins-per-tenant`.

## Installing and removing

| Command | What it does |
|---|---|
| `make` | Installs `MAF_PLUGINS` (unset: every bundled plugin `MAF_ENV` allows, except `_example`) |
| `make core` | The core alone (`MAF_PLUGINS=none`) |
| `make plugins` | Lists every plugin: kind, scope, environments, installed, dependencies |
| `make plugin-new NAME=x KIND=mcp\|app` | Starts a new plugin (above) |
| `make plugin-on NAME=x` | Starts its services, waits until healthy, adds its routes, records the set, tells the running services |
| `make plugin-off NAME=x` | Refuses while it has open work (unless `STOP_WORK=1`, which stops the work through its store first), then the reverse |

`MAF_PLUGINS` is a comma-separated list (dependencies are added), `none`, or unset; with `CI_MODE=1` it is
`CI_PLUGINS`. `MAF_ENV` (`dev` | `qa` | `stage` | `prod`, default `dev`) names the deployment; make and the api refuse a
plugin whose `environments` does not include it, naming the plugin. `plugins/.installed` (git-ignored) is the resolved
set the api, the CopilotKit runtime and the balancer read at run time: switching a remote plugin restarts nothing.
Switching an in-process plugin restarts the api replicas one at a time, each out of the balancer's rotation first; with
one replica it needs `ALLOW_DOWNTIME=1`.

## The seams (in-process plugins)

The core never references a plugin; a plugin reaches the core only through `Maf.Lab.Plugins.Abstractions` (C#) and
`@maf/plugin-api` (web):

- `IMafPlugin` — identity (its name), nothing else;
- `IContributesServices`, `IContributesEndpoints`, `IContributesModel`, `IContributesDomainBehaviour`,
  `IContributesTurnObserver`, `IContributesOpenWork`, `IContributesBrandProvider` — implement only what you give;
- `IMafEndpoints` — your route group (behind the core's gate) and `MapPluginAgent`, the one way to serve an AG-UI agent;
- ports: `IDomainBehaviour`, `ITurnObserver`, `ITurnAccess`, `IConversationStore`, `IInstalledPlugins`,
  `IBrandProvider`. A port reads the caller from the request; none takes a principal, a tenant or a user.
- web: `definePlugin` with `routes`, `nav`, `chatPanes`, `chatSidebars` (content only: the chat draws the sidebar's
  chrome), `turnActions`, `sourceActions`, `cards`, `toolLabels`, `runObservers` and `reviewPanels`; `ChatContext`
  gives a pane the selected turn and the chat's own actions (`openPane`, `setTurnView`, `openConversation`,
  `startNew`). A plugin never navigates by itself (no `react-router`).

A plugin's routes are not served while it is not installed: a path that is the plugin's alone answers `404`, a method
it adds to a core path answers `405`. Its tables exist only while it is installed. Its routes are documented in its
own `docs/http-api.md`; `make docs` transcludes them into `docs/http-api.md`.

## Rules a plugin cannot break

Each is an architecture test (`tests/Maf.Lab.Tests/PluginArchitectureTests.cs`, `PluginContractTests.cs`, and the web's
boundary scanner in `web/src/plugins/plugins.test.tsx`), and the existing AG-UI and tenant query-path tests scan
`plugins/*/server/` as they scan `src/`:

- the core names no plugin: no file outside a plugin's folder holds its path or its project's name, and the api and
  the test hosts take plugin code only through globs;
- no reference to `Qdrant.Client`, `Neo4j.Driver` or AG-UI types: vectors and the graph only through the core's
  tenant-scoped services, AG-UI only through `MapPluginAgent`;
- no tenant parameter anywhere: the tenant comes from the principal only;
- no tool contributed in process (`AIFunctionFactory`, `AITool`): tools reach the agent only through MCP;
- a web part imports only `@maf/plugin-api`, `@maf/shared/…`, packages and its own files (tests may add `@maf/testing`);
- only the official MCP, A2A and AG-UI protocols, with nothing of the plugin's own on the wire.
