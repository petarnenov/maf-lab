# Tasks

No decision-engine call is added or changed, so the Jev review checklist does not apply.

- [x] 1.1 `IContributesDataLifecycle` and the core's own implementation (conversations, turn records, feedback,
      history).
- [ ] 1.2 Orchestrated jobs: user deletion, tenant export and offboarding, per-tenant retention. They show progress, are
      stoppable and resume idempotently. Verify with a stop-and-resume test.
- [ ] 1.3 IdP reconcile through the Keycloak Admin REST API, with the guard against an empty or shrinking list. Verify
      that it refuses on an empty list.
- [ ] 1.35 The store inventory from design: every row implemented, including:
  - Redis keys;
  - `acl` user ids;
  - the mcp-retrieval adjustments ledger (pseudonymised and kept);
  - `PendingWrites`, test-generation and coverage rows;
  - break-glass grants;
  - Keycloak on a direct erasure;
  - the 14-day log rotation.

  The parse cache's row is implemented by `add-document-parsing`. The terminal targets take an operator bearer from
  `MAF_BEARER_TOKEN` (secret convention; sent only as the Authorization header), and compose sets size-based log rotation sized to about 14 days. The architecture test covers core stores too. Legal hold blocks
      retention and offboarding. Each step takes the job's token. An export restarts on resume. Verify the legal-hold
      and ledger scenarios.
- [ ] 1.4 Add the two-users, two-tenants fixture to the plugin contract suite. Verify that nothing of the deleted user
      or tenant remains, and that nothing of the others is gone.

## Working evidence (2026-10-09)

Task 1.1 implemented: IContributesDataLifecycle creates an IDataLifecycle participant with a validated tenant/user
scope, streaming JSON export records, idempotent deletion and tenant retention. The API composes all installed
contributors regardless of tenant enablement and registers its core participant. Core operations include
conversations (also soft-deleted), message history, full turn records, feedback, copied labels and dependent
pending writes. Tenant predicates cover tenant-bearing rows; messages inherit ownership through conversation
IDs. Surviving turns' label flags are recomputed when a departing reviewer owned their labels. Content deletion
and retention use one transaction with the supplied cancellation token at every asynchronous store operation.

Independent review found that the old message-retention worker could delete ownership roots while leaving
copied question labels. It now delegates each tenant's cleanup to the same transactional core participant.
Historical labels whose source turn is already gone and whose subject cannot be recovered cause user export
and erasure to refuse before yielding content or changing data; no owner is guessed and no success is reported.
Tenant-wide deletion can remove these rows. This operational limitation is documented in docs/plugins.md.

Verification: 26 targeted cases pass via the standard dotnet test runner, including nine real-SQLite core cases,
actual API lifecycle composition, installed-but-disabled contribution, existing plugin hosting and the strengthened
retention regression. Tests cover two users/two tenants, foreign-tenant malformed references, idempotence, cutoff
boundaries, reviewer ownership, orphan refusal, pre-cancellation and cancellation after a DELETE with transaction
rollback. Temporarily removing shared-scope rejection made its regression fail; the guard was restored, rebuilt
and the full 26-case selection passed again. Warnings-as-errors build, direct docs synchronization and
git diff --check pass. Both fresh final code/security reviews have zero findings. An alternate direct runner's
ApiFactory stall was terminated; the standard runner's completed gate is the passing evidence.

This completes the content participant/interface task only. No erasure route was added. Authorized jobs, legal-hold
checks, writer quiescence and resumable scheduling remain task 1.2; IdP reconciliation remains 1.3. Audit/grant
pseudonymization and other stores remain 1.35; audit rows and their hash chain were not rewritten here. The broad
plugin store-inventory contract suite remains 1.4/1.35. No index, eval or graph command ran.

Task 1.2 in progress: internal LifecycleJobStore and LifecycleJobRunner now persist immutable operation, scope,
retention cutoff, ordered participant plan and completed-step progress in a separate LifecycleJobs table. The
partial unique tenant index covers queued/running/stopping jobs across operation kinds. Stop requests retain
ownership until the runner awaits the current participant's completion; attempt IDs fence checkpoint and terminal
writes. Destructive resumes keep completed checkpoints; tenant-export resumes restart all steps in a new private
staging generation. No heartbeat timeout releases a potentially live worker. Database read failure in the cancel
watch cancels the executor and waits for its unwind before recording failure. A checkpoint that observes stopping
prevents the next participant from starting, independently of the polling timer.

Verification: 15 new cases cover input validation, durable state, tenant-scoped transitions, two concurrent replicas,
stop/cleanup/resume, real core deletion preserving another user and tenant, failed-step replay, export generations,
host cancellation and watch failure. The final 37-case selection also includes core lifecycle/composition/retention
and table/tenant migration regressions. All pass, as do warnings-as-errors build, direct docs synchronization and
whitespace checks. Temporarily disabling the checkpoint stopping guard made its regression fail with an unwanted
second participant; the guard was restored and all 37 cases rerun successfully. Fresh independent final code and
security reviews have no findings. Frontend is untouched in this increment.

Task 1.2 remains unchecked: this is an internal execution primitive, not an activated erasure service. At that checkpoint, no routes or
hosted jobs registered it yet. Authorization, atomic legal-hold admission, writer quiescence, complete participant
resolution, export bundle storage/publication/expiry, retention scheduling and progress/stop/resume UI integration
still need implementation. Crash recovery must prove the former writer stopped before releasing its slot; elapsed
time alone is insufficient. At that checkpoint, the existing retention worker still needed to join this protocol before holds were exposed.


Next task 1.2 increment: durable tenant/record-type legal holds now share atomic SQLite admission with retention
and offboarding. Enqueue, claim and resume refuse before changing state and return the active hold names. Hold
creation itself refuses while a queued/running/stopping destructive job still owns the tenant; a stop request alone
cannot promise that protection has taken effect. Release is tenant-scoped and idempotent. Export and user-erasure
admission retain the policy stated in this design. No operator hold-management route has been exposed yet.

The existing message-retention timer now admits its core cleanup through the lifecycle journal and awaits the
runner, using the persisted cutoff and the job token. Held/busy tenants are skipped while other tenants proceed;
logs contain hold IDs rather than names. Cancellation after durable enqueue or a transient failure before claim
requests stop with an uncancelled token: an unclaimed queued job becomes stopped, while potentially running work
retains ownership in stopping until unwind is proven. The original failure is preserved.

Verification at the pause checkpoint: 58 targeted cases pass, including 21 new legal-hold cases and the previous
lifecycle, retention and schema migration gates. The new cases include two independent SQLite replicas racing
hold creation and destructive admission, names/type validation, cross-tenant isolation, release, enqueue/resume/
claim refusal, cancellation rollback after SQL mutation, real core retention under a hold, other-tenant progress,
and the committed-enqueue cancellation/transient-claim regressions. Removing the hold-admission guard made 11
regressions fail, including an actual held-tenant purge; the exact original source was restored, rebuilt and all
58 cases passed again. Warnings-as-errors build, direct docs synchronization and git diff --check pass. Fresh
final code/security reviews have no findings. Frontend is untouched. No index, eval or graph command ran.

User requested a pause after a verified stable checkpoint. This increment is finished; task 1.2 remains open for
writer quiescence, complete participant resolution and inventory, export staging/publication/expiry, per-tenant
policies/scheduling, authorized routes, and progress/stop/resume UI and terminal integration. Crash recovery still
must establish that the previous writer stopped. The core timer's hold/admission bypass described at the preceding
checkpoint is now closed. The full OpenSpec objective is paused, not completed.

## Resume checkpoint

Resume phrase: **Продължи OpenSpec**.

The implementation is paused after commit `40208dc` on `introduce-provider-plugins`. Continue the existing
OpenSpec implementation from task `data-lifecycle` 1.2. Durable lifecycle jobs and atomic legal-hold admission
are implemented and verified; the final targeted gate passed 58/58 cases. The next work is writer admission and
quiescence, complete participant resolution, export bundles, per-tenant scheduling and authorized job/UI integration.
There are 36 unchecked tasks across the active, non-archived OpenSpec changes; do not treat this checkpoint as
completion of task 1.2 or of the whole migration.

Keep indexing, reindexing, graph refresh and live evals deferred until the entire agreed code migration is complete.
Use builds, model-free contract tests and docs/spec checks meanwhile. After each completed task, retain a compact
handoff for that task while preserving the full objective and remaining task list.
