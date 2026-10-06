## 1. Move `insights` into `plugins/insights/`

- [ ] 1.1 Create `plugins/insights/` with the statistics screens and routes.
- [ ] 1.2 Remove the allow-list line; update the docs.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
