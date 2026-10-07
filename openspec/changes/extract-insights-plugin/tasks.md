## 1. Move `insights` into `plugins/insights/`

- [x] 1.1 Create `plugins/insights/` with the statistics screens and routes. The aggregators, routes, DTOs and tests
      moved, reading turns through `ITurnRecords` and the guard through `IGuardSettings`; the web part contributes the
      `admin/jev` route (admin) and the **Jev** link.
- [x] 1.2 Remove the allow-list line; update the docs. The routes' rows and the reports' paragraph moved to the
      plugin's `docs/http-api.md`; README marks the screen as plugin `insights`; DECISIONS §81 part J.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside. With the folder present: make test-dotnet 1864 (1863 passed, 1 skipped), vitest 699, docs-check
      in sync (56 routes), validate --strict valid. Moved aside (after make docs): test-dotnet 1817, vitest 679,
      docs-check in sync (54 routes). Rebased onto 5d6dc91: test-dotnet 1864, vitest 699.
- [x] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass. Both green at 9a9ba2b:
      ci-e2e-core (the decline with no domain, 8 AG-UI conformance checks), and ci-e2e with insights in `CI_PLUGINS`
      (verify, a2a-conformance, AG-UI conformance, test generation end to end).
