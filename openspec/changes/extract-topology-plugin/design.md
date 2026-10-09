## Context

See proposal.md. The preparation already exposes ASP.NET Core health checks and installed manifests. Coverage and
telemetry own multiple services, while domain servers have standard catalogue MCP endpoints.

## Goals / Non-Goals

The extraction preserves the existing view and probe budgets while making optional service ownership explicit.
It adds no model calls or index work. Jev requests, prompts, questions, criteria, thresholds and pinned model remain
unchanged; there is no new Jev request to enumerate.

## Decisions

Use installed manifest data for optional nodes. Existing `url` and `label` remain; `service`, `health` and `card`
describe replica HTTP/card checks. Optional `id` preserves diagram vertices. A multi-service owner uses standard
TOML arrays of tables (`[[topology.nodes]]`) with the same node fields. The probe owns behavior, manifests own nodes.
Alternatives: hard-coded service lists duplicate ownership; splitting an app only for its diagram node adds no module.
Record the private manifest fields and rejected Docker socket approach in DECISIONS §81 part M.

Use the official MCP client's discovery and tools/list, forwarding the current bearer token, within each node's
shared deadline. Cache per token. Read shared-state health and reporter identity through HealthCheckService data,
concurrently with all other probes. No home-grown health port is introduced.

Embed the original uncompressed drawing in the plugin assembly. Keep its layout and vertex ids; remove the duplicate
edge list from the wire DTO. The diagram drift guard compares real report vertices from a complete fixture install.
Vector display facts come from the shared bootstrapper's description DTO, including status. Graph failures expose
only type names, except the existing authentication message. These are shared adapters, not driver access.

The web uses the authenticated useApiText hook and AbortSignal. The two existing curriculum links use PageLink only
while their matching route is contributed (D8); they disappear with the screen. The generic screenshot runner skips absent, redirected pages.

## Risks / Trade-offs

A plugin can own nodes absent from the default authored drawing; their states remain in the report. The complete
bundled installation's vertex guard prevents accidental drift. Store and MCP failures remain bounded report data;
request cancellation propagates. Keep protocol fixtures independent of models, live indexes and credentials.

## Migration Plan

Move routes, page, tests and DTOs with the plugin; add owning node tables; remove the core registration and diagram
mount. Run builds, fixtures and docs/spec checks with the folder present and moved aside. Final full-stack/index/eval
validation follows all code migrations, per the owner. Removing the plugin is the rollback for this optional screen.
