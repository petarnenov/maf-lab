# Proposal

## Why

Coverage, the test agent and test generation are dev tools, but their services, screen and tables are core today.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/coverage/` (app, installation, dev and qa): `coverage-runner`, `test-agent`, the testgen AG-UI agent, the
  coverage page, the A2A test-agent admin, their tables (`IContributesModel`) and routes.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `coverage` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

- Terminal: `make coverage` and `make testgen-e2e` show their progress as today.
- Page: the coverage page and a test run show their steps as today.

## Stopping

- Key: Esc on the coverage page; Ctrl+C on `make coverage`
- Stop: a test run stops through its run state, as today
- Recorded in: the run's row and the shared store
- Shown: the run reads stopped

## Documentation impact

- README's Coverage section; `docs/http-api.md` via the plugin's `docs/http-api.md`.
