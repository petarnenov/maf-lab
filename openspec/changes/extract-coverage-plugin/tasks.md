## 1. Move `coverage` into `plugins/coverage/`

- [ ] 1.1 Create `plugins/coverage/` with its services, page, A2A admin, tables and routes.
- [ ] 1.2 Update the docs.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
