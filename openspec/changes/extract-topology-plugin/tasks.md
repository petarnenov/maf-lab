## 0. Batch 1: the core's preparation (D1, D2), landed alone

- [x] 0.1 The shared state is an ASP.NET Core health check (`SharedStateHealth : IHealthCheck`, tagged `shared-state`,
      `Data["instance"]`); `/health` is `MapHealthChecks` in the same JSON; services without shared state call
      `AddInstanceHealth()`; the route check knows `MapHealthChecks` as a GET.
- [x] 0.2 `IInstalledPlugins.Installed()` lists the installed manifests (a default interface method).
- [x] 0.3 The core's probe reads the shared state through `HealthCheckService` and the installed set through
      `IInstalledPlugins`; DECISIONS §81 part M (in progress).
- [x] 0.4 One `make test`, on d2cf246: test-dotnet 1898, vitest 703, all passed; lint-dotnet (warnings as errors),
      lint-web, docs-check (57 routes) and `openspec validate --all --strict` clean.

## 1. Move `topology` into `plugins/topology/` (after coverage and evals land)

- [ ] 1.1 Create `plugins/topology/` with the screen, the probe and its DTOs, the diagram as an embedded resource, and
      `AddGraphStore`; the probe keeps lb, web, api, redis, ollama and chat-provider, and every other node comes from a
      plugin's `[topology]` table (`service`, `health`, `card`); domain servers and their tools through MCP `tools/list`.
- [ ] 1.2 Drop `report.Edges` (the diagram is the single source); `DescribeChunkCollectionAsync` gains the status; the
      graph store's non-authentication failures report the type name only.
- [ ] 1.3 Web: `useApiText` in `@maf/plugin-api`, `apiText` deleted; the curriculum links; the screenshot tool.
- [ ] 1.4 Remove the topology allow-list line, `Topology__DiagramPath` and the `/docs` mount; update the docs and
      DECISIONS (19's addendum, part M).

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
