## 1. Move `curriculum` into `plugins/curriculum/`

- [x] 1.1 Create `plugins/curriculum/` with the screen. The page, its content, its CSS and both tests moved from
      `web/src/curriculum/`; the web part contributes the `curriculum` route and the **Curriculum** link; the manifest
      has no `server/`, `compose.yml` or lb snippets. `curriculum.test.ts` reads the core's routes from
      `web/src/App.tsx` as text and now also asserts that no entry names a `plugins/<x>/` path. The core's layout test
      renders at `/chat`; `curriculum` joins `CI_PLUGINS`. The page's screen links use `PageLink`, which
      `@maf/plugin-api` now exports (a core wrapper over the router's `Link`); a plugins test proves its click goes
      through the core's router without a reload and the core sees the route change.
      - Consequence for later extractions: curriculum may land before evals, topology, index-admin, feedback-review,
        compliance and a2a. Each of those then deletes the `screen` line of its own page's entry in
        `plugins/curriculum/web/curriculum.ts` (as insights and observability do in the core file today). Entries that
        plugins contribute are a later capability change (DECISIONS §81, curriculum's part).
- [x] 1.2 Update the docs. README marks `/curriculum` as plugin `curriculum`; the web-ui delta makes the curriculum
      screen conditional on the plugin and the header's scenarios use it as an example of a long page; DECISIONS §81,
      curriculum's part.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside. With the folder present: make test-dotnet 1860 (one failure, `PluginContractTests`, on a
      DECISIONS line naming the plugin's path; reworded, and the contract class re-run 4/4), vitest 702, docs-check in
      sync (56 routes, 10 pages), validate --strict valid, lint-web clean. Moved aside (after make docs): test-dotnet
      1860, vitest 667, docs-check in sync, validate --strict valid, lint-web clean. Rebased onto b731fce: test-dotnet 1898,
      vitest 704; onto ff516a0 (a2a batch 1 landed): test-dotnet 1902, vitest 702, all passed.
- [x] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass. Both green at 1edd62c:
      ci-e2e-core (the decline with no domain, 8 AG-UI conformance checks), and ci-e2e with curriculum in `CI_PLUGINS`
      (verify, a2a-conformance 10/10, AG-UI conformance 8/8, test generation end to end).
