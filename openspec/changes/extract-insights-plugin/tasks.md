## 1. Move `insights` into `plugins/insights/`

- [x] 1.1 Create `plugins/insights/` with the statistics screens and routes. The aggregators, routes, DTOs and tests
      moved, reading turns through `ITurnRecords` and the guard through `IGuardSettings`; the web part contributes the
      `admin/jev` route (admin) and the **Jev** link.
- [x] 1.2 Remove the allow-list line; update the docs. The routes' rows and the reports' paragraph moved to the
      plugin's `docs/http-api.md`; README marks the screen as plugin `insights`; DECISIONS §81 part J.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
