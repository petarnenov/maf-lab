## 1. Move `portfolio` into `plugins/portfolio/`

- [ ] 1.1 Create `plugins/portfolio/` with its manifest, prompt, `server.json`, compose, lb parts and corpus.
- [ ] 1.1a The billing graph's households come from billing's own accounts seed (no portfolio seed).
- [ ] 1.1b `plugins/qdrant/` (infra), with `depends` from billing, code and portfolio; the topology shows it as not
      installed.
- [ ] 1.1c Core tests: `FixturePortfolioPlugin` and `StandInDomains.Portfolio*`, the `ApiFactory` default with both
      stand-ins, portfolio's own tests moved into the plugin with the drift test, `AllBuiltIn` deleted.
- [ ] 1.1d The web part: portfolio's cards and labels through `cards`/`toolLabels`; billing's labels follow once
      generalize-write-confirmation has created billing's web part.
- [ ] 1.1e `CoreNamesNoDomainTests` scans `src/Maf.Lab.Portfolio` and catches `Maf.Lab.Domain.Portfolio` (after
      generalize-write-confirmation, which edits the same test).
- [ ] 1.2 Delete `Agent__Servers__portfolio__*` and the MCP Inspector's `builtIn` constant.
- [ ] 1.3 Remove `Agent__BuiltInDomains` (compose, `make core`) and `BuiltInDomains.Ids/Descriptors/Behaviours/AddStores`.
- [ ] 1.4 Update the docs and DECISIONS §81.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
