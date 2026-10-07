# Proposal

## Why

The index admin screen and its jobs are core today, and with them the api's reference to the indexing project.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/index-admin/` (app, installation, every environment, depends on `qdrant`): the index admin screen, its six
  `/api/admin` routes (`index/status`, `index/drift`, `index/run`, `index/migrate`, `jobs/{id}`, `jobs/{id}/cancel`) and
  the Api → Indexing reference. The admin job runner, its table and `AdminJob` stay core: coverage shares them until
  extract-coverage. The plugin reaches them through a new `IAdminJobs` port (design D2).
- `Maf.Lab.Indexing` becomes a shared library plugins may reference, beside Domain and Retrieval (D1). The api registers
  retrieval itself (`AddMafRetrievalCore`), and `Maf.Lab.Eval` references Indexing directly (D3).
- Behaviour: a plugin declares its corpus in an optional `[corpus]` table of its manifest (path, collections, layout,
  graph source). billing and portfolio declare theirs. The admin index offers the installed plugins' tenant-layout
  corpora and a run names one (D4, D5, D9); `MAF_ADMIN_INDEX_CORPUS` and its compose line go. The code corpus is never
  offered. A corpus that is not declared, or whose plugin is not installed, is a 404 that changes nothing.
- Behaviour: `plugin-off index-admin` refuses while an index or migrate job is open, or cancels it through the job
  store (`IContributesOpenWork`, D6).
- Behaviour: a corpus whose manifest names no graph source reports its graph drift as not built for it (D7).
- The api→qdrant topology edge is relabelled "search" (D10).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `index-admin` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green. A manifest may declare its corpus.
- `web-ui`: the index administration screen, and stopping it, exist while the index-admin plugin is installed.
- `document-indexing`: the admin drift endpoint exists while the index-admin plugin is installed, and reports the
  graph section only for a corpus with a graph source.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.
- Patterns: the `IAdminJobs` port is a Ports and Adapters (hexagonal) port over the core's job store; the corpus
  declaration is data in the manifest the JSON Schema validates; per-corpus services are built with .NET's
  `ActivatorUtilities` and `Options.Create`, as `DomainChunkStore.For` already builds a plugin's store.
- Own: the manifest's `[corpus]` table and `IInstalledPlugins.Corpora()` (no standard describes a plugin's corpus);
  recorded in DECISIONS §81 with the alternatives rejected (design D4).

## Progress

- Page: an index or migrate job shows its progress as today, naming its corpus; the drift card shows its loading state.

## Stopping

- Key: Esc on the index admin screen; `make plugin-off NAME=index-admin` for open jobs
- Stop: the job's cancel route moves its row in the admin job store to canceled, as today; the replica running it
  watches that row. `plugin-off` cancels through the same store.
- Recorded in: the job's state in the shared store
- Shown: "Stopping…" until the job reports canceled, then canceled with how far it got

## Documentation impact

- README's index admin mentions; `docs/http-api.md` via the plugin's `docs/http-api.md`; `docs/plugins.md` (the
  `[corpus]` table replaces the transitional `MAF_ADMIN_INDEX_CORPUS` note); `docs/topology.drawio` (edge label);
  DECISIONS §81, the next free part at landing.
