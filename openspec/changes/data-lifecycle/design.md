# Design

## Context

This is decision 5w, taken with the user on 2026-10-05 and moved here from `introduce-plugins`. It needs
`introduce-plugins` (plugins, the contract suite) and `adopt-company-idp` (users and tenants from the IdP).

### 5w. Every store of tenant or user data has a lifecycle (decided with the user, 2026-10-05)

A plugin that keeps tenant or user data SHALL implement `IContributesDataLifecycle`:
- `ExportAsync(tenant | user)`
- `DeleteAsync(tenant)`
- `DeleteAsync(user)`
- `ApplyRetentionAsync(policy)`

A plugin without it may keep no such data, and the architecture test flags a plugin that registers a store without it.
The core implements the same interface for its own data: conversations, turn records, feedback and history.

**The core orchestrates.** Every orchestrated run is a job in the api's store. It shows progress (progress-feedback)
and can be stopped (stop-anything). Every step is idempotent, so a stopped or failed run resumes where it left off and
never leaves half a user behind. These runs are:
- a SCIM user deletion, or an erasure request, runs `DeleteAsync(user)` on every installed plugin and on the core;
- tenant offboarding runs `ExportAsync` (when the customer asks for its data) and then `DeleteAsync(tenant)`;
- retention policies per tenant (the operator sets the allowed range, the tenant admin the value) run on a schedule.

**Audit records** are kept for their own legal period after an erasure. They never hold content (5c, 5n) and refer to
the deleted user only by a pseudonymous id.

**Verified by the contract suite (3.7).** For every plugin it writes fixture data for two users and two tenants,
deletes one user and one tenant, and checks that nothing of theirs is left and nothing of the others is gone.

### Store inventory (re-review 10)

Every store holding tenant or user data is listed here, with its action and legal basis. The architecture test fails
for any store registered without a lifecycle implementation, in the core as well as in plugins.

| Store | Holds | On user erasure | On tenant offboarding | Basis |
|---|---|---|---|---|
| api SQLite: conversations, turns, messages | conversation content | delete | export, then delete | GDPR Art. 17 / 20 |
| api SQLite: turn records and traces | decisions, sources, (dev) traces | delete | delete | Art. 17 |
| api SQLite: feedback and labels | ratings, reasons | delete | export, then delete | Art. 17 / 20 |
| api SQLite: admin jobs | job metadata, no content | pseudonymise the user | delete | Art. 17 |
| api SQLite: A2A tasks, push configs | task state, partner calls | delete the user's | delete | Art. 17 |
| mcp-retrieval SQLite `/app-data/maf-lab-adjustments.db`: the applied-adjustments ledger (billing writes, `docker-compose.yml:169`) | confirmed fee adjustments | **kept**, user pseudonymised | **kept** under legal hold, then per schedule | Art. 17(3)(b): legal obligation for financial records |
| api SQLite: `PendingWrites` | adjustments awaiting confirmation, with `UserId` | delete | delete | Art. 17 |
| api SQLite: `TestGenRuns`, coverage snapshots, files and thresholds | repository test-generation and coverage data, with the requesting user | pseudonymise the user | not tenant data (installation-scoped, dev and qa only) | Art. 17 |
| api SQLite: break-glass grants (`adopt-company-idp`) | operator ids, reasons, times | keep: they are audit records | keep for the audit period | Art. 17(3)(b), (e) |
| api SQLite: audit | who did what, no content | pseudonymise | kept for its legal period | Art. 17(3)(b), (e) |
| Redis: run state, A2A tasks, focus, idempotency | short-lived state that may carry question text | delete the user's keys | delete the tenant's keys | Art. 17 |
| Qdrant / Neo4j: chunks and nodes | tenant documents; `acl` with user ids | remove the user's id from every `acl` | delete the tenant's collections' points and nodes | Art. 17 |
| Parse cache (added by `add-document-parsing`, which applies after this change and adds its own row's implementation) | parsed documents | — (per document, not user) | delete the tenant's entries | Art. 17 |
| Brand rows and asset blobs (added by `add-white-labeling`, which applies after this change and implements its row) | a tenant's brand and logos | — (tenant data, not user) | delete the tenant's brand | Art. 17 |
| copilot-runtime memory: per-thread events and the `runBy` map (`server.ts:7-10,40`) | a run's events while it streams | transient: held in process memory only, gone on completion or restart; never persisted | same | not stored |
| Logs: application logs (`ToolAudit.cs:22` logs principal and tenant ids) and the lb access log (`nginx.conf:21`, remote address and request line) | ids and addresses, never content (project rule) | exempt from per-user deletion | exempt | kept about 14 days, then rotated out. The mechanism: compose sets `logging: {driver: local, options: {max-size, max-file}}` on every service, sized from the measured daily volume to hold about 14 days; Docker's drivers rotate by size, not age, so the sizing is reviewed with the volume. Legitimate interest (security and operations), with the rotation as the retention limit |
| Keycloak user | identity | delete through the Admin REST API on a direct (non-SCIM) erasure; on a SCIM delete it is already gone | the organization is removed by the platform | Art. 17 |
| Export bundle | the export itself | — | written to the tenant's export location, deleted after 7 days or on download confirmation | Art. 20 |
| Plugin stores | each plugin's own data | through `IContributesDataLifecycle` | same | per plugin |

**Explicitly exempt**, besides the logs above, because they hold no message content by project rule: Jaeger and OTel traces, Prometheus metrics,
and eval reports and datasets. Any eval case built from a tenant's real turns is itself a plugin store and is not
exempt.

### Retention and legal hold

Retention follows a records schedule in the sense of ISO 15489: a record type, its retention period, and its
disposition. The operator sets the allowed range, and the tenant admin sets the value. A **legal hold**, set by the
operator on a tenant or a record type, stops retention and offboarding from deleting what it covers. Both refuse and
name the hold.

### Running a job

- Every store step receives the job's `CancellationToken`, since a tenant-wide Qdrant delete can run for minutes, and
  stops at its next batch.
- Deletion steps resume. **An export restarts from scratch** on resume, because a half-written bundle is never offered.

### Terminal targets and their token

`make tenant-export TENANT=` and `make tenant-offboard TENANT=` run as an operator. They take a bearer from the
environment, `MAF_BEARER_TOKEN`. The `_TOKEN` suffix keeps it under the Makefile's secret rule (`COMPOSE_ENV_SECRET`,
`Makefile:29`), so make never imports it from `compose/.env`. A value set in the shell is still inherited by make's recipes, as any
environment variable is. It is sent only as the
`Authorization` header and never logged or echoed. Where it comes from:
- in dev, it is the output of `make dev-token PERSONA=operator TENANT=<tenant> AUDIENCE=api` (the operator persona of
  `enable-plugins-per-tenant`), exported as `MAF_BEARER_TOKEN`;
- in prod, the operator obtains a token for the tenant's organization through the platform dashboard's sign-in.

They call the same job routes as the platform dashboard and cancel through the same route on Ctrl+C. No new IdP
client or grant is needed (fourth audit, item 5).

### How the system learns a user has left

The customer's directory provisions users into Keycloak with SCIM. Keycloak has no supported outbound webhook, so the
api reconciles. A scheduled job lists the tenant's users through Keycloak's Admin REST API (supported) and starts a
deletion for each user who is gone. No custom Keycloak extension (SPI) is written. A user's own erasure request starts
the same job directly.

## Risks / Trade-offs

- [A deletion that half-completes] → every plugin step is idempotent, and the job records each finished store. A rerun
  skips those.
- [A reconcile that mistakes an IdP outage for "everyone left"] → the job refuses to delete when the IdP's user list is
  empty or shrinks by more than a set share, and alerts instead.
