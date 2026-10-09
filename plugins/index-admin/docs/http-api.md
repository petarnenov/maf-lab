Index administration (`PLATFORM_ADMIN`, otherwise `403`) over the corpora the installed plugins declare in their
manifests' `[corpus]` tables, for the caller's readable tenants. Only `tenants`-layout corpora are offered. `corpus`
names one by its plugin; with it absent and exactly one offered, that one is used, with several offered it is `400`
naming the choices, and a corpus no installed plugin offers is `404` and starts nothing.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/platform/index/corpora` | — | `[{ name, hasGraph }]`, the corpora offered, by plugin name |
| GET | `/api/platform/index/status?corpus=` | — | `IndexStatus` for that corpus's collection: `{ modelVersions, activeDenseVector, currentJob }` |
| GET | `/api/platform/index/drift?corpus=` | — | `DriftReport` for that corpus: the index against the source, plus `graph` (add-graph-drift) — `{ available, reason, outOfSync, outOfSyncPercent, missingFromGraph, behind, notInCorpus }`, the corpus's graph against the same source documents; `available: false` with `reason: "unreachable"` when Neo4j cannot be read, `"not-built"` when the corpus has no graph |
| POST | `/api/platform/index/run` | `{ corpus? }` | `202 AdminJob`; a corpus whose folder is absent runs, says so and removes nothing |
| POST | `/api/platform/index/migrate` | `{ corpus?, targetModel? }` | `202 AdminJob` |
| GET | `/api/platform/jobs/{jobId}` | — | `AdminJob` |
| POST | `/api/platform/jobs/{jobId}/cancel` | — | `202` with the `AdminJob`, now `canceled`, while it stops; `409` when it had already ended; `404` for a job not of the admin's tenant. Any replica takes it: the job's row is the stop, and the replica running the job watches it (stop-anything). Once the work has stopped, its `summary` says how far it got |

`AdminJob.state`: `queued` \| `running` \| `succeeded` \| `failed` \| `canceled`. One `index` and one `migrate` job run
per tenant at a time, whichever corpus; a job's progress and summary name its corpus. `run` and `migrate` accept `{}`
or no body.

The screen contributes the `index-admin` section to `/platform?section=index-admin`. Its guarded standalone
route `/admin/index` remains available when the platform dashboard shell is absent. Both require `PLATFORM_ADMIN`.
