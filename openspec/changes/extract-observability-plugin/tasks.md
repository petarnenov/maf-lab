## 1. Move `observability` into `plugins/observability/`

- [ ] 1.1 Create `plugins/observability/` with the three services, their routes and the telemetry screen.
- [ ] 1.2 Update the docs.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
