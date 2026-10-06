# Proposal

## Why

The telemetry stack (otel-collector, prometheus, jaeger) and its screen are core today, though without them the services simply export nothing.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/observability/` (infra/app, installation, every environment): `otel-collector`, `prometheus`, `jaeger`,
  their lb routes and the telemetry screen. Without it, services export nothing, as today when the endpoint is unset.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `observability` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

- Page: the telemetry screen shows its loading state as today.

## Stopping

None — the change moves code; whatever it moves stops as it does today (its own `stopping` in the manifest says how).

## Documentation impact

- README's telemetry section; `docs/telemetry.md`.
