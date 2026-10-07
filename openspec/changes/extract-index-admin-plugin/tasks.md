## 1. The plugin folder (design D2, D4, D5, D7, D8, D9)

- [ ] 1.1 `plugins/index-admin/plugin.toml` (app, installation, every environment, `depends = ["qdrant"]`, progress and
      stopping) and `server/Maf.Lab.Plugins.IndexAdmin.csproj` (Abstractions, Domain, Retrieval, Indexing).
- [ ] 1.2 `IndexAdminPlugin` (`IContributesServices` → `AddMafIndexing`; `IContributesEndpoints`; `IContributesOpenWork`
      over the `index` and `migrate` jobs).
- [ ] 1.3 The six routes plus `GET /api/admin/index/corpora`, behind `PolicyNames.TenantAdmin`, on `IAdminJobs`; the
      corpus by name, a 404 that changes nothing for an undeclared or uninstalled corpus, only tenant-layout corpora,
      the one-corpus fallback and the 400 naming the choices.
- [ ] 1.4 Per-corpus pipeline, drift and migration services (`ActivatorUtilities`, `Options.Create`); drift's graph
      section from the corpus's graph source, "not built" without one.
- [ ] 1.5 Web: `index.ts` (admin route, admin-only nav link), the screen with its corpus picker, `jobs.ts`, `types.ts`,
      the moved tests.
- [ ] 1.6 Tests: `AdminIndexApiTests` moved to `tests/unit/`, with and without the plugin (404 without); corpus
      selection, the undeclared/uninstalled 404, the code corpus not offered, the declared-but-absent corpus removing
      nothing; open work listed and cancelled.
- [ ] 1.7 `docs/http-api.md` of the plugin.

## 2. The core's side (last, after compliance, observability, insights and curriculum land)

- [ ] 2.1 `IAdminJobs` and `AdminJobCancelResult` in the abstractions, the core's implementation over `AdminJobRunner`;
      coverage's cancel route on `AdminJobCancelResult`; `AdminIndexEndpoints` and `app.MapAdminIndex()` removed.
- [ ] 2.2 `Program.cs` calls `AddMafRetrievalCore`; the Api → Indexing reference removed; `Maf.Lab.Eval` references
      Indexing; `PluginArchitectureTests` allows Indexing for plugins.
- [ ] 2.3 The `[corpus]` table: `plugin.schema.json`, `PluginManifest.CorpusTable`, `IInstalledPlugins.Corpora()`,
      `plugins.py validate` rejecting a collection two installed manifests name; billing and portfolio declare theirs;
      `MAF_ADMIN_INDEX_CORPUS`, its compose line and billing's export removed.
- [ ] 2.4 `DriftService` takes its graph source from its options.
- [ ] 2.5 Web core: `Progress` to `web/src/shared/`; `AdminJob`/`AdminJobState` from `@maf/plugin-api`; the route and
      nav link out of `App.tsx` and `Layout.tsx`; the curriculum's two entries unlinked; `RequireAdmin.test` and
      `plugins.test` fixed.
- [ ] 2.6 Topology edge relabelled "search" (probe and drawio, ids kept); `verify_lb.sh` 4.4 and its handler under
      `plugin_in_use("index-admin")`; `CI_PLUGINS` gains index-admin.
- [ ] 2.7 Docs: `docs/http-api.md` rows out and `make docs`; README; `docs/plugins.md`; DECISIONS §81, the next free
      part.

## 3. Verify

- [ ] 3.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 3.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
