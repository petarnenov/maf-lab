# Proposal

## Why

The eval reports screen, its routes and `make eval*` are dev tools, but they are core today, and the eval host builds the code server itself.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/evals/` (app, installation, dev and qa): the eval reports screen and routes, `make eval*`, `DatasetWriter`
  and the A2A probe. Suites and cases come from the installed plugins: this change adds per-plugin discovery and moves billing's rows
  (selection, retrieval, generation, injection, confirmation) and code's out of `evals/`.
- The eval host reaches the code server through the catalogue's endpoint instead of `BuildApp`, which frees
  `src/Maf.Lab.CodeSearch`, the graph-tool tests (GraphBuildAndTool, GraphTraceEvent, GraphIntegration) and the code
  graph builder to move under the code plugin's folder.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `evals` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

- Terminal: `make eval*` show their bars as today.
- Page: the eval reports screen shows its loading state as today.

## Stopping

- Key: Ctrl+C on `make eval*`
- Stop: as today, at a safe point between cases (exit 130), keeping what finished
- Recorded in: the eval's report file
- Shown: the run says it was stopped and how many cases finished

## Documentation impact

- README's Evals sections; `docs/http-api.md` via the plugin's `docs/http-api.md`.
