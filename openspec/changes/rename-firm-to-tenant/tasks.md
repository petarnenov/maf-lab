# Tasks

No decision-engine call is added or changed, so the Jev review checklist does not apply.

## 1. Code

- [x] 1.1 Rename `Principal.FirmId` → `TenantId`, the `firm_id` claim → `tenant_id` (keeping `firm_id` as an alias for
      one release), `FIRM_ADMIN` → `TENANT_ADMIN`, and the `FirmAdmin` policy, across `src/`, `web/src`, `tests/`,
      `copilot-runtime`, `compose` and `scripts`. Verify that `make test` and `make lint` pass.

- [x] 1.2 Domain roles and attributes:
  - `Principal` becomes `(UserId, TenantId, CoreRole)` with `TENANT_ADMIN`, `USER` and `READ_ONLY`;
  - `ADVISOR`, `OPS` and advisor ids become the `domain_roles` and `advisor_ids` claims, read only by mcp-retrieval;
  - the dev personas carry them;
  - `/api/me` drops advisor ids.

  `AllowedAdvisorIds` has no isolation role today (the review found no advisor-scoped retrieval), so add a test that
  pins `/api/me` and the dev personas' claims before and after, rather than claim tests that do not exist.
- [x] 1.3 Web: drop `advisorIds` from `api/types.ts` and `DevTokenPicker.tsx`, and show domain roles only where a
  domain plugin needs them. Verify with Vitest.

## 2. Stored data

- [x] 2.1 A rename pass in `DatabaseInitializer`, run **before** the additive pass. It renames each `FirmId` column
      to `TenantId` where the old name exists and the new one does not, and drops the old `IX_*_FirmId_*` indexes.
      Verify on a database from before the change that every row keeps its tenant (none becomes ''), that the
      `TenantId` indexes exist (the `AdminJobs` one unique and filtered), and that a second start changes nothing.
      Each table's check and rename run in one `BEGIN IMMEDIATE`, and a lost race is tolerated. Verify with two
      initialisers started at once on one database.
- [x] 2.2 The trace and run-state readers accept `firmId` and `tenantId`. Verify that a stored trace and a Redis run
      state from before the change still read with their tenant.
- [x] 2.3 Rename `firmId` in `evals/*.jsonl`, `tools/Maf.Lab.A2AProbe`, `tools/screenshots/capture.mjs` and
      `compose/mcp-inspector/start.mjs`. Verify that `make eval SUITE=selection` holds its baseline and that the probe
      passes.

## 3. Documents

- [x] 3.1 Update CLAUDE.md, project.md (then `make docs`), copilot-instructions, `docs/http-api.md` (the `/dev/token`
      body, the `/api/me` shape), `docs/trace-events.md` and the README. At archive, edit the `tenant-isolation` spec's
      Purpose, which a delta cannot change, so that it says "tenant". Every
      `tenant-isolation` requirement is renamed by this change's MODIFIED deltas. Other specs that say "firm" for the
      tenant get their own MODIFIED deltas here before apply. Verify that `make docs-check` passes.
