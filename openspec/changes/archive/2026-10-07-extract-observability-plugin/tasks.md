## 1. Move `observability` into `plugins/observability/`

- [x] 1.1 Create `plugins/observability/` with the three services, their routes and the telemetry screen.
      The services, volume and config (`files/otel/`), the lb snippets, the screen and `/api/telemetry` (server part),
      the browser's tracing (web `activate`), and the trace link through the `ITraceLink` port (Null Object in the
      core). The api's `Telemetry__*` keys are `compose/env/observability.env`; the export address keeps its default
      (DECISIONS §81 part I). The topology shows the three as not installed without it.
- [x] 1.2 Update the docs. `docs/telemetry.md` keeps the signals; where they go and the route moved to the plugin's
      `docs/`; README, project.md and config.yaml service lists; generated blocks; DECISIONS §81 part I.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside. With the folder present, `make test-dotnet` ran 1865 tests with 1 failure: the known TestGen timing
      flake, `TestGenRunsApiTests.A_run_goes_from_the_agent_to_a_verified_candidate` ("verifying" where "candidate" was
      expected), in code this change does not touch, and it was not re-run. Moved aside, it ran 1858 with 0 failures.
      Web vitest passed 703 tests with the folder and 692 aside. docs-check is in sync both ways (aside after make docs),
      and validate --strict passes. Rebased onto 5c04ce4 (compliance landed): test-dotnet 1876, vitest 703. Rebased
      onto e4d44b8 (insights landed): test-dotnet 1880, vitest 701, all passed.
- [x] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass. Both were green on 3ebc02f, one run
      each. In the core-only stack's logs, `otlp|collector` appears nowhere (DECISIONS §81 part I).
