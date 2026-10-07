# Proposal

## Why

Portfolio is the second built-in domain. After it moves, the core holds no domain at all, and the knob that turns built-in domains off selects from nothing.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/portfolio/` (mcp, tenant scope, every environment): manifest with `[domain]` from
  `BuiltIn/portfolio/`, `prompt.md`, `server.json`, its compose service (`mcp-portfolio`), lb parts, corpus
  (`data-portfolio/` → `files/corpus/`) and seed (`files/seed/`), `plugin.mk`, its in-process part (`PortfolioBehaviour`
  and its chunk store, through a library factory so it never reaches the Qdrant client), and its web part: the three
  data cards and four tool labels, through the existing `cards` and `toolLabels` contributions. A card renderer is now
  given the question, the focus and a way to set it (additive). The generic formatters move to `@maf/shared/format`.
- `src/Maf.Lab.Portfolio` stays where it is until extract-evals-plugin, as billing's host does: the eval builds it in
  process.
- The billing graph reads each account's household from billing's own seed instead of portfolio's (each bounded
  context owns its data, sharing identifiers only). The graph keeps the same nodes and edges.
- `BuiltInDomains.CardResultTypes` is deleted: the core reads only a card's activity type, and the "a card holds no free
  text" walk is the plugin's own test over its own DTOs.
- No domain is built in any more: `DomainCatalogue.AllBuiltIn` is gone, and outside any scope the catalogue is empty.
- `qdrant` becomes an infra plugin folder (installation, every environment), moved here from extract-billing-plugin,
  once no core service needs it: `billing`, `code` and `portfolio` gain `depends` on it, and `make core` stays green
  without it.
- Removed: `Agent__Servers__portfolio__*` in `compose/docker-compose.yml` (the same shadowing as billing's); the MCP
  Inspector's `builtIn` constant entirely.
- Removed with it (no behaviour change: `MAF_PLUGINS=none` already yields no domain): `Agent__BuiltInDomains` in the
  core compose file and in `make core`; `BuiltInDomains.Ids`, `Descriptors`, `Behaviours` and `AddStores`.
  `BuiltInDomains.cs` itself stays until extract-compliance-plugin (`LegacyCapabilities`).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `portfolio` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.

## Progress

- Terminal: `make index` shows the indexer's bar for the portfolio corpus as today, through the plugin's `plugin.mk`.
- Page: the chat's portfolio turns and data cards show their steps as today.

## Stopping

None — the change moves code; whatever it moves stops as it does today (its own `stopping` in the manifest says how).

## Documentation impact

- README and `docs/plugins.md`: portfolio's sections; `make core` loses `Agent__BuiltInDomains`.
- DECISIONS §81: the built-in domain knob is gone.
