## 1. The plugin folder (design D2, D4, D5, D7, D8, D9)

- [x] 1.1 `plugins/index-admin/plugin.toml` (app, installation, every environment, `depends = ["qdrant"]`, progress and
      stopping) and `server/Maf.Lab.Plugins.IndexAdmin.csproj` (Abstractions, Domain, Retrieval, Indexing).
- [x] 1.2 `IndexAdminPlugin` (`IContributesServices` → `AddMafIndexing`; `IContributesEndpoints`; `IContributesOpenWork`
      over the `index` and `migrate` jobs).
- [x] 1.3 The six routes plus `GET /api/admin/index/corpora`, behind `PolicyNames.TenantAdmin`, on `IAdminJobs`; the
      corpus by name, a 404 that changes nothing for an undeclared or uninstalled corpus, only tenant-layout corpora,
      the one-corpus fallback and the 400 naming the choices.
- [x] 1.4 Per-corpus pipeline, drift and migration services (`ActivatorUtilities`, `Options.Create`); drift's graph
      section from the corpus's graph source, "not built" without one.
- [x] 1.5 Web: `index.ts` (admin route, admin-only nav link), the screen with its corpus picker, `jobs.ts`, `types.ts`,
      the moved tests.
- [x] 1.6 Tests: `AdminIndexApiTests` moved to `tests/unit/`, with and without the plugin (404 without); corpus
      selection, the undeclared/uninstalled 404, the code corpus not offered, the declared-but-absent corpus removing
      nothing; open work listed and cancelled.
- [x] 1.7 `docs/http-api.md` of the plugin.

## 2. The core's side (last, after compliance, observability, insights and curriculum land)

- [x] 2.1 `IAdminJobs` and `AdminJobCancelResult` in the abstractions, the core's implementation over `AdminJobRunner`;
      coverage's cancel route on `AdminJobCancelResult`; `AdminIndexEndpoints` and `app.MapAdminIndex()` removed.
- [x] 2.2 `Program.cs` calls `AddMafRetrievalCore`; the Api → Indexing reference removed; `Maf.Lab.Eval` references
      Indexing; `PluginArchitectureTests` allows Indexing for plugins.
- [x] 2.3 The `[corpus]` table: `plugin.schema.json`, `PluginManifest.CorpusTable`, `IInstalledPlugins.Corpora()`,
      `plugins.py validate` rejecting a collection two installed manifests name; billing and portfolio declare theirs;
      `MAF_ADMIN_INDEX_CORPUS`, its compose line and billing's export removed.
- [x] 2.4 `DriftService` takes its graph source from its options.
- [x] 2.5 Web core: `Progress` to `web/src/shared/`; `AdminJob`/`AdminJobState` from `@maf/plugin-api`; the route and
      nav link out of `App.tsx` and `Layout.tsx`; the curriculum's two entries unlinked; `RequireAdmin.test` and
      `plugins.test` fixed.
- [x] 2.6 Topology edge relabelled "search" (probe and drawio, ids kept); `verify_lb.sh` 4.4 and its handler under
      `plugin_in_use("index-admin")`; `CI_PLUGINS` gains index-admin.
- [x] 2.7 Docs: `docs/http-api.md` rows out and `make docs`; README; `docs/plugins.md`; DECISIONS §81, the next free
      part.

## 3. Verify

- [x] 3.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
      Evidence (one run each, under `systemd-inhibit --what=idle`): present, on bc47abd: .NET 1873/1873, vitest
      703/703 (93 files), docs-check in sync (57 routes), `openspec validate --all --strict` 67/67. Moved aside, after
      `make docs`: .NET 1848/1848, vitest 691/691 (90 files), docs-check in sync (50 routes), validate 67/67; the
      folder restored and `make docs` left the tree clean. No flake. 10b0461 after it only rewords two api comments so
      they name no plugin. Rebased onto 33747d9 (compliance, insights and observability landed): test-dotnet 1893,
      vitest 703, all passed.
- [x] 3.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
      Evidence (one run each, on 10b0461): `ci-e2e-core` passed — every service healthy, the no-domain turn declines
      without a tool, 8/8 AG-UI conformance checks. `ci-e2e` passed — 42 checks, none failed, among them "an installed
      plugin declares a corpus" (`[{"name":"billing","hasGraph":true}]`), "second start while running returns the same
      job", "job finishes succeeded" and "job status was served by >= 2 replicas"; 8/8 AG-UI conformance checks.
