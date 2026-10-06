# Plugins

The core of maf-lab is a domain-agnostic assistant shell. Everything optional is a plugin: a domain the assistant
answers about, an agent it consults, a screen, a provider, a dev tool. A plugin is **one folder**; deleting the folder
removes it from every part of the system. Design: `openspec/changes/introduce-plugins/design.md` (decisions 1-10,
"Principles and patterns").

## A plugin folder

```
plugins/<name>/
  plugin.toml          # the manifest (required): checked against plugins/plugin.schema.json
  server.json          # an MCP plugin's server, in the official MCP Registry format
  compose.yml          # its own services only; never adds to api, lb or copilot-runtime
  lb.http.conf         # its nginx upstreams (included in http{})
  lb.server.conf       # its nginx locations, exact matches (included in server{})
  plugin.mk            # its make targets (included by the Makefile)
  server/              # Maf.Lab.Plugins.<Name>.csproj: code that runs in the api (in-process plugins only)
  web/index.ts         # definePlugin({...}): code that runs in the web app (in-process plugins only)
  tests/               # its tests, which go with it
```

Only `plugin.toml` is required. Two kinds of plugin cover almost everything:

- **Remote plugins** (`mcp`, `a2a`) add a capability over a protocol the project already speaks: an MCP server or an
  A2A agent, in any language. They need no C# and no React. Start here.
- **In-process plugins** (`app`) add api routes (`server/`) or screens (`web/`). Use one only for a screen or a route.

A `provider` plugin implements something the core needs exactly one or more of (a decision engine, a chat model,
embeddings); an `infra` plugin is a store or a dev tool.

## The manifest

```toml
schema       = 1
name         = "weather"                # the folder's name
kind         = "mcp"                    # mcp | a2a | app | provider | infra
scope        = "tenant"                 # tenant (a tenant may choose it) | installation (the whole deployment)
environments = ["dev", "qa", "stage", "prod"]
description  = "Forecasts for the account's region"
depends      = []                       # other plugins it needs; installed first
# public     = true                     # listed by /api/plugins before sign-in (only a sign-in plugin needs this)
progress     = "None — every call is short"
stopping     = "None — no long work"

[domain]                                # an MCP plugin: how the core uses its tools (operator-reviewed data)
id            = "weather"
tools         = ["forecast"]            # the allow-list; the endpoint comes from server.json
search_tool   = "forecast"
guard_context = "Questions about weather in an account's region are in scope."
routing       = ["Is the question about the weather?"]

[agent]                                 # an app plugin that serves an AG-UI agent
name = "weather-agent"
path = "/api/weather/agent"

[topology]                              # where the topology screen probes it
url   = "http://weather:8080/health"
label = "weather"
```

`progress` and `stopping` take the forms every proposal takes (`progress-feedback`, `stop-anything`): `None — <reason>`,
`Terminal: …` and/or `Page: …`; for stopping, `Key: …; Stop: …; Recorded in: …; Shown: …`. `make docs-check` fails a
manifest without them, with an unknown key, or with a dependency that does not exist.

A remote plugin's server is described by its own standard document, never restated in the manifest: `server.json`
(MCP Registry) for an MCP server, the Agent Card for an A2A agent. Its routing questions, guard context and card types
live in the manifest, which the operator reviews, and are never read from the remote side, so a third party cannot
shape a decision or the system prompt.

## Installing and removing

| Command | What it does |
|---|---|
| `make` | Installs `MAF_PLUGINS` (unset: every bundled plugin `MAF_ENV` allows, except `_example`) |
| `make core` | The core alone (`MAF_PLUGINS=none`) |
| `make plugins` | Lists every plugin: kind, scope, environments, installed, dependencies |
| `make plugin-on NAME=x` | Starts its services, waits until healthy, adds its routes, records the set, tells the running services |
| `make plugin-off NAME=x` | Refuses while it has open work (unless `STOP_WORK=1`, which stops the work through its store first), then the reverse |

`MAF_ENV` (`dev` | `qa` | `stage` | `prod`, default `dev`) names the deployment; make and the api refuse a plugin whose
`environments` does not include it, naming the plugin. `plugins/.installed` (git-ignored) is the resolved set the api,
the CopilotKit runtime and the balancer read at run time: switching a remote plugin restarts nothing. Switching an
in-process plugin restarts the api replicas one at a time, each out of the balancer's rotation first; with one replica
it needs `ALLOW_DOWNTIME=1`.

## The seams (in-process plugins)

The core never references a plugin; a plugin reaches the core only through `Maf.Lab.Plugins.Abstractions`:

- `IMafPlugin` — identity (its name), nothing else;
- `IContributesServices`, `IContributesEndpoints`, `IContributesModel`, `IContributesDomainBehaviour`,
  `IContributesTurnObserver`, `IContributesOpenWork`, `IContributesBrandProvider` — implement only what you give;
- `IMafEndpoints` — your route group (behind the core's gate) and `MapPluginAgent`, the one way to serve an AG-UI agent;
- ports: `IDomainBehaviour`, `ITurnObserver`, `IConversationStore`, `IBrandProvider`.

Its routes answer `404` whenever it is not installed. Its tables exist only while it is installed.

## Rules a plugin cannot break

Each is an architecture test (`tests/Maf.Lab.Tests/PluginArchitectureTests.cs`), and the existing AG-UI and tenant
query-path tests scan `plugins/*/server/` as they scan `src/`:

- no reference to `Qdrant.Client`, `Neo4j.Driver` or AG-UI types: vectors and the graph only through the core's
  tenant-scoped services, AG-UI only through `MapPluginAgent`;
- no tenant parameter anywhere: the tenant comes from the principal only;
- no tool contributed in process (`AIFunctionFactory`, `AITool`): tools reach the agent only through MCP;
- only the official MCP, A2A and AG-UI protocols, with nothing of the plugin's own on the wire.
