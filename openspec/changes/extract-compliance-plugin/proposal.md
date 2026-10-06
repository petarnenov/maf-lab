# Proposal

## Why

The compliance reviewer, its client and the audit screen are still core, so a deployment without a reviewer still carries them, and fee adjustment's `tool_requires` still resolves through configuration.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/compliance/` (a2a, tenant scope): the reviewer agent and its compose service, the client, the
  `/compliance` route, and the audit **screen** and its routes. The audit **records** (`ToolAudit`, `AuditKinds`,
  `AuditChain`) stay core (5y).
- `tool_requires: compliance` resolves to "the compliance plugin is in use" instead of `Compliance:BaseUrl`; the
  consultant moves behind the port extract-billing-plugin added. (No contradiction with §81: the fee adjustment code
  moved with billing; this only changes how its requirement resolves.)
- Removed: `BuiltInDomains.LegacyCapabilities`, and with it `BuiltIn/` and the scanner's `BuiltIn/` exemption.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `compliance` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

- Page: the audit screen shows its loading state while a page of the record loads, as today.

## Stopping

None — the change moves code; whatever it moves stops as it does today (its own `stopping` in the manifest says how).

## Documentation impact

- README's Compliance section and `docs/http-api.md` (the routes, via the plugin's `docs/http-api.md`).
- DECISIONS §81: `tool_requires` by plugin.
