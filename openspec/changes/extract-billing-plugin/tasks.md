## 1. Move `billing` into `plugins/billing/`

- [ ] 1.1 Create `plugins/billing/` with its manifest, prompt, `server.json`, compose, lb parts and `plugin.mk`.
- [x] 1.2 Split `src/Maf.Lab.Retrieval`: deferred to extract-evals-plugin (with CodeSearch). The host stays as residue,
      allow-listed in `CoreNamesNoDomainTests`, which now scans the library too; its two leaks are fixed.
- [ ] 1.3a Core tests: `FixtureBillingPlugin` + `StandInDomains.Billing*`, the `ApiFactory` default, billing's own
      tests moved into the plugin, the drift test; the retrieval acceptance tests on a small corpus of no domain's.
- [ ] 1.3 Move fee adjustment (`BillingBehaviour`, `FeeAdjustmentFlow`, `PendingAdjustments`) and add the reviewer-consultation port.
- [ ] 1.4 Create `plugins/neo4j/` and add `depends` to `neo4j-browser`, `code` and `billing`; keep `make core` green
      without it (`plugins/qdrant/` moves with extract-portfolio-plugin).
- [ ] 1.5 Make Jev's text domain-generic and re-measure the guard, intent and answer-check suites (in a subagent).
- [ ] 1.6 Delete `Agent__Servers__billing__*` and the MCP Inspector's billing line; derive the topology's domain-server nodes from the catalogue.
- [ ] 1.7 Move the billing corpus, templates and eval cases; update the docs and DECISIONS §81.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
