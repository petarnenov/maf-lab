# Tasks

No decision-engine call is added or changed, so the Jev review checklist does not apply.

- [ ] 1.1 `IContributesDataLifecycle` and the core's own implementation (conversations, turn records, feedback,
      history).
- [ ] 1.2 Orchestrated jobs: user deletion, tenant export and offboarding, per-tenant retention. They show progress, are
      stoppable and resume idempotently. Verify with a stop-and-resume test.
- [ ] 1.3 IdP reconcile through the Keycloak Admin REST API, with the guard against an empty or shrinking list. Verify
      that it refuses on an empty list.
- [ ] 1.35 The store inventory from design: every row implemented, including:
  - Redis keys;
  - `acl` user ids;
  - the mcp-retrieval adjustments ledger (pseudonymised and kept);
  - `PendingAdjustments`, test-generation and coverage rows;
  - break-glass grants;
  - Keycloak on a direct erasure;
  - the 14-day log rotation.

  The parse cache's row is implemented by `add-document-parsing`. The terminal targets take an operator bearer from
  `MAF_BEARER_TOKEN` (secret convention; sent only as the Authorization header), and compose sets size-based log rotation sized to about 14 days. The architecture test covers core stores too. Legal hold blocks
      retention and offboarding. Each step takes the job's token. An export restarts on resume. Verify the legal-hold
      and ledger scenarios.
- [ ] 1.4 Add the two-users, two-tenants fixture to the plugin contract suite. Verify that nothing of the deleted user
      or tenant remains, and that nothing of the others is gone.
