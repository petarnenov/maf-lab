# Proposal

## Why

Jev and intent statistics are a dev tool (5i), but they are core today.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/insights/` (app, installation, dev and qa, not in the product image): the Jev and intent statistics
  screens and routes, reading the core turn record.
- Two read ports in `Maf.Lab.Plugins.Abstractions` (`CoreReads.cs`), implemented by the core: `ITurnRecords` (the
  caller's tenant's turns' core records since a time, for a tenant admin, the tenant from the principal) and
  `IGuardSettings` (the guard's effective thresholds). The statistics' DTOs leave `Maf.Lab.Domain` with them. The routes
  require the core's tenant-admin policy by its name, `PolicyNames.TenantAdmin` (extract-compliance-plugin's).
- Behaviour: the old `/admin/intents` link, which redirected to `/admin/jev`, is gone; like any unknown path it now
  lands on the chat. The **Jev** link moves after the core's links in the main navigation, and the curriculum's
  router entry no longer links a screen.
- Removed: its line (`JevStatistics`) from the core-names-no-domain allow-list.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `insights` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

- Page: the statistics screens show their loading state as today.

## Stopping

None — the change moves code; whatever it moves stops as it does today (its own `stopping` in the manifest says how).

## Documentation impact

- README's statistics section; `docs/http-api.md` via the plugin's `docs/http-api.md`.
