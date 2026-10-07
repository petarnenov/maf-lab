## 1. Move `portfolio` into `plugins/portfolio/`

- [x] 1.1 Create `plugins/portfolio/` with its manifest, prompt, `server.json`, compose, lb parts and corpus.
- [x] 1.1a The billing graph's households come from billing's own accounts seed (no portfolio seed).
- [x] 1.1b `plugins/qdrant/` (infra), with `depends` from billing, code and portfolio; the topology shows it as not
      installed.
- [x] 1.1c Core tests: `FixturePortfolioPlugin` and `StandInDomains.Portfolio*`, the `ApiFactory` default with both
      stand-ins, portfolio's own tests moved into the plugin with the drift test, `AllBuiltIn` deleted.
- [x] 1.1d The web part: portfolio's cards and labels through `cards`/`toolLabels`; billing's labels follow once
      generalize-write-confirmation has created billing's web part.
- [x] 1.1e `CoreNamesNoDomainTests` scans `src/Maf.Lab.Portfolio` and catches `Maf.Lab.Domain.Portfolio` (after
      generalize-write-confirmation, which edits the same test).
- [x] 1.2 Delete `Agent__Servers__portfolio__*` and the MCP Inspector's `builtIn` constant.
- [x] 1.3 Remove `Agent__BuiltInDomains` (compose, `make core`) and `BuiltInDomains.Ids/Descriptors/Behaviours/AddStores`.
- [x] 1.4 Update the docs and DECISIONS §81.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [x] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.

Evidence (one run each, the user's rule):
- `make test-dotnet`: 1860 with the folder present and 1801 with it moved aside; web vitest 701 and 688; tsc and lint
  clean; docs-check in sync both ways; `openspec validate --strict`.
- `make ci-e2e-core`: all checks passed (after 4b9ecaf, which makes the admin index job's check depend on the vector
  store being installed).
- `make ci-e2e`: all checks, every AG-UI and A2A conformance scenario and testgen-e2e passed on acb119a, after two fixes it
  found: make up's first conf.d stage was not core-only (edadd20), and the A2A replica-spread check ran inside nginx's
  fail_timeout window (acb119a).
