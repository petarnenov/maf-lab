# Design

## Context

The index admin is the `/admin/index` screen and six `TENANT_ADMIN` routes under `/api/admin`: `index/status`, `index/drift`,
`index/run`, `index/migrate`, `jobs/{id}` and `jobs/{id}/cancel`. Today it is core:

- **Routes:** `src/Maf.Lab.Api/Endpoints/AdminIndexEndpoints.cs`.
- **Screen:** `web/src/admin/`.
- **Indexing reference:** the api's reference to `Maf.Lab.Indexing` exists for these routes.

Five things in the code shape the move:

1. **Plugins may not reference Indexing.** `PluginArchitectureTests.No_plugin_references_the_core` forbids plugins from
   referencing `Maf.Lab.Indexing`, so no plugin can take the reference as the rule stands.
2. **The job runner is shared with coverage.**
   - Coverage's refresh is an admin job on the same runner (DECISIONS: "A refresh is an admin job"). It also maps its
     cancel through `AdminIndexEndpoints.Canceled`.
   - The runner's methods take a `string tenantId`, which no plugin method may.
3. **Indexing registration also sets up retrieval.** `AddMafIndexing` is the api's only call to `AddMafRetrievalCore`, and
   chat, A2A, topology, feedback and coverage use what that call registers.
4. **Eval reaches Indexing through the api.** `Maf.Lab.Eval` has no reference of its own.
5. **Only one corpus is reachable.**
   - The admin index has one corpus, `Indexing__CorpusRoot: /plugins/${MAF_ADMIN_INDEX_CORPUS}`, exported by billing's
     `plugin.mk`. It has one collection, the configured default `maf_chunks`/`maf_meta`, which is billing's.
   - Its drift compares only with the billing graph.
   - DECISIONS §81 part F left this as transitional: "index-admin replaces the variable with each plugin's corpus from its
     manifest".

## Goals / Non-goals

**Goals**

- `plugins/index-admin/` holds the screen, the six routes and the reference to `Maf.Lab.Indexing`.
  - With the folder present, an index job runs, reports progress and can be cancelled as before.
  - With the folder moved aside, nothing of it remains, and `make test` and `make docs-check` pass.
- Each plugin that owns a corpus declares it in its manifest. The admin index offers the corpora of the installed plugins
  and indexes the one the run names.
- `plugin-off index-admin` refuses while an index or migrate job is open.

**Non-goals**

- Moving the job runner, its table or `AdminJob`. Coverage shares them, so they stay core until `extract-coverage-plugin`.
- Making the manifest the single source of make's corpus variables (`BILLING_ENV`, `PORTFOLIO_ENV`, `CODE_ENV`). In this
  change `plugin.mk` keeps them for the CLI; that move is a follow-up.
- Reindexing the code corpus from the screen (D5).
- Moving `Maf.Lab.Indexing`, or its billing and code graph builders, out of the core.

## Decisions

### D1. Plugins may reference `Maf.Lab.Indexing`

**Decision**

- `Maf.Lab.Indexing` leaves the forbidden list of `No_plugin_references_the_core` and joins the shared libraries plugins
  may use, beside `Maf.Lab.Domain` and `Maf.Lab.Retrieval`. This is the same move extract-a2a makes for `Maf.Lab.A2A`.
- It stays in `CoreAssemblies`, so the core still references no plugin.

**Why:** Indexing is a library whose public surface is a pipeline (index, drift, migrate). It is not the api's internals,
which is what the rule protects.

**Rejected**

- (b) A core `IIndexAdmin` port implemented by the api. It keeps the api's Indexing reference, which this change moves, and
  it puts index admin back in the core under another name.
- (c) Moving Indexing into the plugin. make, the indexer CLI, Eval, and the billing, portfolio and code tests all use it.

### D2. Jobs stay core behind an `IAdminJobs` port

**Decision**

- `AdminJobRunner`, `JobCancelWatch`, the `AdminJobs` table and `AdminJob`/`AdminJobStates` stay core. Coverage shares
  them, and stop-anything says the store that owns a job's state owns its stop.
- `Maf.Lab.Plugins.Abstractions` gains `IAdminJobs`, implemented by the core over the runner:

  ```csharp
  public interface IAdminJobs
  {
      Task<AdminJob> StartAsync(string kind, Func<IProgress<string>, CancellationToken, Task<string>> work, CancellationToken ct);
      Task<AdminJob?> GetAsync(string jobId, CancellationToken ct);
      Task<AdminJobCancelResult> CancelAsync(string jobId, CancellationToken ct);
      Task<AdminJob?> CurrentAsync(CancellationToken ct);
      Task<IReadOnlyList<AdminJob>> OpenAsync(IReadOnlyCollection<string> kinds, CancellationToken ct);   // D6
      Task CancelOpenAsync(IReadOnlyCollection<string> kinds, CancellationToken ct);                      // D6
  }
  ```

- **Tenant:** each call takes the tenant from the request's principal, inside the core's implementation. No method has a
  tenant parameter: CLAUDE.md, and `PluginArchitectureTests`' parameter-name rule.
- **Cancel result:**
  - `AdminJobCancelResult` carries the outcome (canceling, already ended, not found) and the job, and maps itself to
    202/409/404 through `ToResult()`.
  - It replaces the static `AdminIndexEndpoints.Canceled`, so neither the plugin nor coverage copies the mapping.
  - Coverage's cancel route moves to it. Coverage otherwise keeps the concrete runner, with its `_repository` scope,
    until extract-coverage.
- **Routes:** `jobs/{id}` and `jobs/{id}/cancel` move with the plugin. Coverage has its own refresh and cancel routes, and
  its screen does not use these.
- **Single flight:** jobs keep their kinds, `index` and `migrate`, and so one running job per tenant and kind, held by the
  table's unique index across replicas. Runs over different corpora queue behind each other, as runs on one corpus do
  today. The `ollama-batch` instance is pinned to its own CPUs, so a parallel second run would only contend for them. The
  job's progress and summary name the corpus.
  - Rejected: a kind per corpus (`index:billing`), which would run several embedders on the batch instance's pinned CPUs
    at once.

### D3. Services: the core registers retrieval, the plugin registers indexing

**Decision**

- The api calls `AddMafRetrievalCore` itself, where it called `AddMafIndexing`.
- The plugin's `IContributesServices` calls `AddMafIndexing`. Its registrations are `TryAdd`, so the second
  `AddMafRetrievalCore` inside it changes nothing.
- `Maf.Lab.Eval` gets its own `ProjectReference` to `Maf.Lab.Indexing`. Eval is a tool, not core or plugin, so the plugin
  rule does not apply to it.

### D4. A plugin declares its corpus in its manifest

**Decision**

- `plugins/plugin.schema.json` gains an optional `corpus` table. The fields:

  | Field | Required | Meaning |
  |---|---|---|
  | `path` | yes | Relative to the plugin's folder, never leaving it. |
  | `collection` | yes | The Qdrant chunk collection. |
  | `meta_collection` | yes | Where its BM25 model lives. Every corpus has one beside its collection, so the table names both. |
  | `layout` | no | `tenants` (the default) or `repository`. |
  | `graph` | no | The graph source its documents are built into. |

- billing declares `files/corpus`, `maf_chunks`, `maf_meta` and `graph = "billing"`. portfolio declares `files/corpus`,
  `maf_portfolio_chunks` and `maf_portfolio_meta`.
- `PluginManifest` gains the matching `CorpusTable`.
- **Port:** `IInstalledPlugins` gains `IReadOnlyList<PluginCorpus> Corpora()`, the corpora of the installed plugins. Each
  carries:
  - the plugin's name;
  - its root, made absolute by the core from `Plugins:Root`, which the core already knows;
  - its collections, layout and graph source.

  `Corpora()` is read from the catalogue's current set, so it follows `plugin-on` and `plugin-off` like `IsInstalled`.
- **Per-corpus services:** for each run the plugin builds the pipeline, drift and migration services over the corpus's
  options. It uses `ActivatorUtilities.CreateInstance` with `Options.Create` copies of `QdrantOptions` and
  `IndexingOptions`, the way `DomainChunkStore.For` builds a plugin's store today. Every Qdrant read and write still goes
  through `TenantScopedMaintenance`, the one tenant-filtering query builder (CLAUDE.md).
- **Variable removed:** `MAF_ADMIN_INDEX_CORPUS`, the compose line that passed it and billing's export of it are removed.
  The api already mounts `../plugins` read-only at `Plugins:Root`.

**Rejected**

- Keeping one corpus through the variable, moved into the plugin's compose fragment. That keeps billing hard-wired, and
  part F promised otherwise.
- A corpus list in index-admin's own configuration. It would name other plugins outside their folders.
- Exposing the whole manifest through the port. The plugin needs only the corpora (interface segregation).

### D5. Which corpora a tenant admin may index, and the tenant guard

**Decision**

- The screen and the routes offer only the installed plugins' corpora with the `tenants` layout. A run names one corpus
  by its plugin's name.
- A corpus that is not declared, or whose plugin is not installed, is a 404 that does nothing. The check happens before
  any job starts.
- The `repository` layout (code) is never offered. It belongs to the installation, not to a tenant, and one tenant's
  administrator must not rebuild it.
- The a85b74b guard stays as it is: a run over a missing corpus root, or for a tenant without a folder, removes nothing.
  - Its integration test keeps it.
  - A plugin test adds the declared-but-absent case: the manifest names a path that is not there.
- The scope stays the principal's readable tenants (own plus shared), as today.

### D6. `plugin-off` refuses during a job

**Decision**

- The plugin implements `IContributesOpenWork` over `IAdminJobs.OpenAsync` and `CancelOpenAsync` for the kinds `index`
  and `migrate`. `plugin-off index-admin` then lists and refuses, or cancels through the job store, as it does for other
  long work.
- Coverage's `coverage.refresh` jobs are not this plugin's, and they are untouched.
- Unlike the other `IAdminJobs` calls, these two are installation-wide: they run with no request, so they take no tenant
  and read none.
- Behaviour line in the proposal.

### D7. Drift: the graph section only for a corpus with a graph source

**Decision**

- `DriftService` reads the graph source from its options instead of the constant `GraphSources.Billing`.
- A corpus whose manifest names no `graph` gets a report whose graph section reads "not built for this corpus". The
  screen shows it in the drift card's theme, as it shows "unavailable".
- billing reports exactly as today.

### D8. The web part

**Decision**

- `plugins/index-admin/web/index.ts` registers `admin/index` with `PluginRoute.admin` (the core's admin guard, as insights
  does) and its nav link, shown to everyone as the core's link was. `App.tsx` and `Layout.tsx` lose them.
- Imports: the screen imports only `@maf/plugin-api`, `@maf/shared/…` and its own files. Of the core components it uses:
  - `StopHint`, `useEscToStop` and `format` are already in `@maf/shared`.
  - `Progress` moves from `web/src/components/` to `web/src/shared/`, and its core importers follow.
  - `useAuth` is not needed once the route is behind the admin guard.
- `AdminJob` and `AdminJobState` are exported from `@maf/plugin-api`, as the TypeScript half of the `IAdminJobs` seam.
  The core's coverage screen keeps using them. The drift, status and corpus types move to the plugin's `types.ts`.
- The curriculum's two `screen: { to: '/admin/index' }` entries lose their link, by parts I and J. If the curriculum has
  moved to `plugins/curriculum/web/` by then, the edit is made there. The page links nowhere else, so `PageLink` is not
  needed.
- The screen gains a corpus picker. It lists `GET /api/admin/index/corpora`, and when only one corpus is installed it
  shows that one without a choice.

### D9. Routes

**Decision**

- Paths stay under `/api/admin`, behind `PolicyNames.TenantAdmin`. That name comes from extract-compliance, which lands
  first.
- The corpus is a query parameter on the reads and a body field on the writes. The existing paths and job resources keep
  their shape:

  | Method | Path | Change |
  |---|---|---|
  | GET | `/api/admin/index/corpora` | new: the offered corpora, by plugin name |
  | GET | `/api/admin/index/status?corpus=` | the corpus's collection's model versions, and the tenant's current job |
  | GET | `/api/admin/index/drift?corpus=` | that corpus's drift |
  | POST | `/api/admin/index/run` `{ corpus }` | runs that corpus |
  | POST | `/api/admin/index/migrate` `{ corpus, targetModel? }` | migrates that corpus's collection |
  | GET | `/api/admin/jobs/{id}` | unchanged |
  | POST | `/api/admin/jobs/{id}/cancel` | unchanged |

- **Fallback:** with `corpus` absent and exactly one corpus offered, that corpus is used. Today's single-corpus callers,
  `scripts/verify_lb.sh` among them, keep working.
- With `corpus` absent and several offered, the answer is a 400 that names the choices.

### D10. Core residue and documentation

**Decision**

- **Topology:** the api→qdrant edge is relabelled "search" in `TopologyProbe` and in `docs/topology.drawio`. The edge ids
  stay, because `TopologyTests` parses the drawio.
- **Load balancer check:** `scripts/verify_lb.sh` 4.4 and its signal handler's admin-job cancel run only under
  `plugin_in_use("index-admin")`, as 4.5 and billing's checks do.
- **Core specs:**
  - web-ui "Index administration screen" and "Index administration can be stopped" gain "while the index-admin plugin is
    installed" through MODIFIED deltas (the curriculum ruling).
  - document-indexing "Drift reporting" gets the same delta, because it says "An admin endpoint … SHALL report".
  - stop-anything is unchanged. Its admin-screen scenario and its store requirement are about any admin job, and coverage
    still has one.
- **API docs:** the routes go to `plugins/index-admin/docs/http-api.md` and leave `docs/http-api.md`. `make docs` writes
  the generated block.
- **Prose docs:** README's index-admin mentions and `docs/plugins.md`'s transitional note are updated.
- **DECISIONS:** §81 takes the next free part letter at landing.

## Risks / Trade-offs

- **Two-step rebase.** `IInstalledPlugins` and `PluginManifest` change in the core, and other in-flight extractions touch
  the same files.
  - Mitigation: the core edits come last, after compliance, observability, insights and curriculum land, on the rebased
    branch.
- **Duplicate collection names.** A plugin could declare a collection another plugin also uses.
  - Mitigation: `plugins.py validate` rejects two installed manifests that name the same collection.
- **Several corpora queue behind one job** (D2). The screen shows the running job, and its corpus, to explain the wait.

## Progress

- Page: an index or migrate job shows its progress on the screen as today, and names its corpus. The drift card shows its
  loading state while the report is read.

## Stopping

- Key: Esc on the index admin screen.
- Stop: the job's cancel route moves its row in the admin job store to canceled, and the replica running it watches that
  row. `plugin-off` cancels open jobs through the same store.
- Recorded in: the job's state in the shared store.
- Shown: the screen reads "Stopping…" until the job reports canceled, then shows it canceled with how far it got.
