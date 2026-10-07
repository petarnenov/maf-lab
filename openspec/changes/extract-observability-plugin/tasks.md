## 1. Move `observability` into `plugins/observability/`

- [x] 1.1 Create `plugins/observability/` with the three services, their routes and the telemetry screen.
      The services, volume and config (`files/otel/`), the lb snippets, the screen and `/api/telemetry` (server part),
      the browser's tracing (web `activate`), and the trace link through the `ITraceLink` port (Null Object in the
      core). The api's `Telemetry__*` keys are `compose/env/observability.env`; the export address keeps its default
      (DECISIONS §81 part I). The topology shows the three as not installed without it.
- [x] 1.2 Update the docs. `docs/telemetry.md` keeps the signals; where they go and the route moved to the plugin's
      `docs/`; README, project.md and config.yaml service lists; generated blocks; DECISIONS §81 part I.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
