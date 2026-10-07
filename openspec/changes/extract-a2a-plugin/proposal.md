# Proposal

## Why

The assistant's A2A surface (its agent card, partner tokens and the billing agent handler) is core today, though a deployment may have no partner.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/a2a/` (app, tenant scope): `A2A/BillingAgentCard.cs`, `BillingAgentHandler.cs` (renamed for what they
  are), the partner-token routes and their compose/lb parts. The card's skills and tags come from the catalogue.
- The library `Maf.Lab.A2A` stays core.
- `a2a-inspector` gains `depends = ["a2a"]` (§10, 5o).
- Removed: its line from the core-names-no-domain allow-list.
- The deployment's outbound A2A client registrations generalize from `Compliance:*` (`compose/env/compliance.env`,
  extract-compliance-plugin) to `A2A:Clients:<agent>` (client id and secret per agent consulted), mirroring
  `A2A:Partners:<id>` for the inbound side. Its endpoint defaults from the agent's manifest (`[agent]`, or its card)
  through the catalogue, as an MCP plugin's endpoint does. Client credentials are issued per authorization server, so
  they stay the deployment's configuration, not a plugin folder's.
- Also: `PartnerIdentity`'s default store keyspace (`compliance`, live Redis data) and `A2AAdminEndpoints`' agent
  fallback, left as they were by extract-compliance-plugin.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `a2a` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

None — the change moves code; every command and page it touches shows progress as it does today, through its new folder.

## Stopping

- Key: the partner's own client
- Stop: an A2A `tasks/cancel`, as today, through the run state store
- Recorded in: the run's state in the shared store
- Shown: the task reads `canceled`

## Documentation impact

- README's A2A section; `docs/http-api.md` via the plugin's `docs/http-api.md`.
