## 1. Move `billing` into `plugins/billing/`

- [x] 1.1 Create `plugins/billing/` with its manifest, prompt, `server.json`, compose, lb parts and `plugin.mk`.
      Done in 34ec5ee (part 2): the manifest with the `[domain]` table, prompt, server.json, compose and CI override,
      lb parts, env file, seeds and corpus, and plugin.mk. plugin.mk exports the seed paths and the admin index's corpus.
- [x] 1.2 Split `src/Maf.Lab.Retrieval`: deferred to extract-evals-plugin (with CodeSearch). The host stays as residue,
      allow-listed in `CoreNamesNoDomainTests`, which now scans the library too; its two leaks are fixed.
- [x] 1.3a Core tests: `FixtureBillingPlugin` + `StandInDomains.Billing*`, the `ApiFactory` default, billing's own
      tests moved into the plugin, the drift test; the retrieval acceptance tests on a small corpus of no domain's.
      Done in 34ec5ee; the drift test is `The_core_fixture_is_this_plugins_domain`.
- [x] 1.3 Fee adjustment: `BillingBehaviour`, the tool and the ledger moved; the reviewer consultation is the port
      `IReviewerConsultation` (8c76061). Deferred to generalize-write-confirmation, which comes right after this
      change (the user's order): `FeeAdjustmentFlow`, `ConfirmationService`, `ConfirmationSink`
      (`CapturedConfirmation`), `ChatTurnRunner`'s proposal, `RunRejoin`, `HistoryEndpoints`, `ToolSource`,
      `PendingAdjustmentRow` in `MafDbContext`, `MessageRetentionService`, `AuditChain`, `Program.cs`. The fee-typed
      ones are allow-listed in `CoreNamesNoDomainTests`. Order from here: extract-billing →
      generalize-write-confirmation → extract-portfolio → extract-compliance → extract-a2a → extract-feedback-review →
      introduce-provider-plugins → … The fee ledger's pooled-connection race was fixed on the way (57780fc), and the
      api's store was unpooled likewise (7af05bf).
- [x] 1.4 Create `plugins/neo4j/` and add `depends` to `neo4j-browser`, `code` and `billing`; keep `make core` green
      without it (`plugins/qdrant/` moves with extract-portfolio-plugin). Done in 34ec5ee: the topology shows the graph
      store as not installed. `make ci-e2e-core` is green without it, and `make up` writes a plugin's lb snippets only
      once its services are healthy (c6c7b2d).
- [x] 1.5 Make Jev's text domain-generic and re-measure the guard, intent and answer-check suites (in a subagent).
      Done in 67990ea: the `subject` and `[domain.intent]` keys, measured once each (guardrail 20261007-044900, accepted
      as its baseline; answer-check 20261007-045113; intent 20261007-045128), with the thresholds unchanged (DECISIONS
      §81 part F).
- [x] 1.6 Delete `Agent__Servers__billing__*` and the MCP Inspector's billing line; derive the topology's domain-server
      nodes from the catalogue. The compose lines (34ec5ee), the api's own `Agent:Servers:billing` in appsettings.json,
      which shadowed the manifest's server (6094b56), and the inspector's line are gone. A domain server's node already
      reads its endpoint and tools from the catalogue and shows a domain that is not installed as such. The fixed node
      list stays with `TopologyProbe`, allow-listed, until extract-topology-plugin.
- [x] 1.7 Move the billing corpus, templates and eval cases; update the docs and DECISIONS §81. The corpus and seeds
      moved (34ec5ee). The graph templates and builder stay core until the template-contribution seam (extract-evals);
      billing's eval cases stay in `evals/` until extract-evals-plugin. README, docs/plugins.md and §81 part F are updated.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside. With the folder present: make test-dotnet 1841, with one known TestGen flake (Joe's) and one fixed
      assertion. With it moved aside: 1731, 0 failed. docs-check is in sync both ways, and validate --strict passes.
- [x] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass: ci-e2e-core was green after
      794b7ed, and ci-e2e after c6c7b2d, including a2a-conformance.
