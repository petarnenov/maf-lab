# Proposal

## Why

The topology screen and probe are core today, and the probe still names the billing, portfolio and codebase domains
(its line of the core-names-no-domain allow-list).

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/topology/` (app, installation, dev and qa): the topology screen and probe (`/api/topology` and
  `/api/topology/diagram`), their DTOs, and `AddGraphStore`, which the core registered only for the probe.
- Removed: the topology line of the core-names-no-domain allow-list. The `CoreNamesNoDomainTests` scanner stays for good
  as the core's guard.
- Lands after compliance, observability, a2a, coverage and evals, so the services those move are plugin nodes by then.
- Core preparation, a small change of its own before the move (D1, D2):
  - the shared-state check is an ASP.NET Core health check (`IHealthCheck`, tagged `shared-state`, with
    `Data["instance"]` the instance's name), and `/health` is mapped with `MapHealthChecks` in the same JSON shape, so
    there is one health mechanism;
  - `IInstalledPlugins.Installed()` lists the installed plugins' manifests (operator-reviewed public data).
- Seams: the plugin reads health through `HealthCheckService` (D1); finds domain servers among the installed plugins
  with an MCP endpoint and lists their tools with the official MCP C# client's `tools/list`, one call per server,
  forwarding the caller's bearer token (never logged) within today's 2 s budget (D2); `@maf/plugin-api` exports
  `useApiText` beside `useApi` and `useUserKey`, and `apiText` leaves `web/src/api/client.ts` (D5).
- Behaviour: the diagram is an embedded resource of the plugin's server assembly, not `docs/topology.drawio`;
  `Topology__DiagramPath` and the api's `../docs:/docs:ro` mount go (D3).
- Behaviour: the `[topology]` manifest table gains optional `service` (the DNS name whose replicas are probed),
  `health` (the path asked) and `card` (an A2A card path). Each plugin declares its own node there; the probe keeps
  only the core's own nodes: lb, web, api, redis, ollama and chat-provider (D4). Adding `[topology]` lines to other
  plugins' manifests is plugin data, not core naming.
- Behaviour: the report carries no edges; the diagram is their single source, as DECISIONS 19 already says. The
  vertex drift test stays, in the plugin (D6).
- Behaviour: the vector store's facts come from `CollectionBootstrapper.DescribeChunkCollectionAsync`, which gains the
  collection's status; a graph-store failure other than authentication reports only the exception's type name, never
  the driver's message (D7).
- The curriculum's `/topology` entries link through `PageLink` if it exists by then, else lose the link (D8); the
  screenshot tool keeps its entry and skips a page that is not served, and README's section says "(plugin `topology`)"
  (D9).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `topology` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.
- Standards: ASP.NET Core Health Checks (`IHealthCheck`, `HealthCheckService`, `MapHealthChecks`) for what only the
  core can see; MCP `tools/list` through the official C# client for a domain server's tools; .NET embedded resources
  for the diagram. Rejected: a home-grown health port, and asking each host's `/health` for redis (not visible per
  host).
- Own: the `[topology]` table's `service`, `health` and `card` fields (no standard describes how a plugin's service is
  probed); recorded in DECISIONS with the alternatives rejected: Docker healthcheck labels (they need the Docker socket,
  decision 19) and ASP.NET health checks (in-process only); DECISIONS §81 (its part at landing).

## Progress

- Page: the topology screen shows its loading state as today.

## Stopping

None — the change moves code; whatever it moves stops as it does today (its own `stopping` in the manifest says how).

## Documentation impact

- README's topology section (the diagram's new place) and screens list; `docs/http-api.md` via the plugin's
  `docs/http-api.md`; `docs/plugins.md` (the `[topology]` fields, the new seams); DECISIONS 19 gains an addendum
  rejecting the static edge list, and a new entry records D1 (the health checks, rejecting a home-grown port and per-host
  `/health`) and the `Own:` `[topology]` fields.
