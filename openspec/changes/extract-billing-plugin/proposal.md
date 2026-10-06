# Proposal

## Why

Billing, the lab's first domain, is still built into the core (`src/Maf.Lab.Api/BuiltIn/billing`, the `mcp-retrieval` host, the billing corpus, collection and graph templates). Until it is a plugin, the core is not domain-agnostic, and enable-plugins-per-tenant and introduce-provider-plugins cannot treat it like any other domain.

This is one of the follow-up extractions introduce-plugins (task 8.1) opened, in this order: `extract-billing-plugin`, `extract-portfolio-plugin`, `extract-compliance-plugin`, `extract-a2a-plugin`, `extract-coverage-plugin`, `extract-evals-plugin`, `extract-insights-plugin`, `extract-feedback-review-plugin`, `extract-index-admin-plugin`, `extract-observability-plugin`, `extract-topology-plugin`, `extract-curriculum-plugin`. It moves
code; it changes no behaviour, except where a line below says so.

## What Changes

- `plugins/billing/` (mcp, tenant scope, every environment): its manifest with the `[domain]` table from
  `BuiltIn/billing/domain.json` and `prompt.md`; `server.json` for `mcp-retrieval`; the corpus, collection and graph
  templates; the billing eval cases; its compose service and lb parts.
- The split of `src/Maf.Lab.Retrieval` (5g): the library stays core (tenant-scoped search, BM25, embeddings, the dev
  JWT, the relevance judge's client), which the api and the code server reference; only the host moves —
  `Program.cs`, the `Dockerfile`, `Billing/` and the relevance judge's server side become `plugins/billing/service/`.
  The proposal's design names each file on either side.
- Fee adjustment moves with billing (§81, 5g): `BuiltIn/BillingBehaviour.cs`, `Agent/FeeAdjustmentFlow.cs`, and
  `PendingAdjustments` as `IContributesModel`. `FeeAdjustmentFlow` calls `A2A/ComplianceConsultant`, an Api type, so
  this change adds **one port** in `Maf.Lab.Plugins.Abstractions` for the reviewer consultation, implemented by the core
  until extract-compliance-plugin: the one non-move. The generic write confirmation stays core:
  `ConfirmationService`, `ChatTurnRunner`, `RunRejoin`, `HistoryEndpoints`.
- `qdrant` and `neo4j` become two infra plugin folders (installation, every environment), with `depends`:
  `neo4j-browser` → `neo4j`; `code` → `qdrant`, `neo4j`; `billing` → `qdrant`, `neo4j` (`ollama-embeddings` comes with
  introduce-provider-plugins). `make core` and `make ci-e2e-core` stay green with neither store: the api's health
  must not require a store.
- Jev's text becomes domain-generic (design part B, decision 6), and the guard, intent and answer-check suites are
  re-measured — in a subagent that returns only the conclusion.
- Removed: `Agent__Servers__billing__*` in `compose/docker-compose.yml` (they would shadow the manifest's server); its
  line in the MCP Inspector's `start.mjs`.
- The topology's domain-server nodes come from the catalogue (part C #10); `TopologyProbe.cs` still names codebase, so
  it stays allow-listed until extract-topology-plugin.
- Not taken: `A2A/BillingAgentCard.cs` and `BillingAgentHandler.cs` — despite the name, they are the assistant's A2A
  surface (extract-a2a-plugin).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `plugins`: the catalogue gains the `billing` plugin; deleting its folder removes it and keeps `make test` and
  `make docs-check` green.

## Principles

- SOLID: the core keeps only the seams the plugin uses (`Maf.Lab.Plugins.Abstractions`, `@maf/plugin-api`); the plugin
  depends on them, never the reverse (dependency inversion), and the core gains no branch for it (open/closed).
- Standards: the plugin contract of introduce-plugins (spec `plugins`): a manifest, the official MCP, A2A and AG-UI
  protocols, `IContributes*` (Orchard Core's module shape), compose fragments with only the plugin's own services.
- Own: the reviewer-consultation port in Maf.Lab.Plugins.Abstractions — a seam no standard defines; DECISIONS §81.

## Progress

- Terminal: `make index` and `make graph` show the indexer's bar for the billing corpus as today, through the plugin's `plugin.mk`.
- Page: the chat's billing turns show their steps as today.

## Stopping

- Key: Ctrl+C on `make index`/`make graph`; Esc on the chat page
- Stop: as today — the indexer stops at a safe point (exit 130), a run stops through CopilotKit's stop
- Recorded in: the indexer's run state; the run's state in the shared store
- Shown: the bar ends with "stopped"; the turn says it was stopped

## Documentation impact

- `docs/plugins.md`: billing as the worked example of a domain plugin, and `qdrant`/`neo4j` with `depends`.
- README: the billing sections say they need the plugin; the generated blocks.
- DECISIONS §81: the Retrieval split and the reviewer-consultation port.
