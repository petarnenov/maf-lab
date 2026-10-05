# Design

## Context

- The goal is a universal enterprise assistant (decision 5q, with the user, 2026-10-05), deployed as multi-tenant SaaS
  and as single-tenant installations from one codebase. Its core must not speak the TAMP domain's language.
- `Principal` today is `Principal(string UserId, TenantId FirmId, Role Role, IReadOnlyList<string> AllowedAdvisorIds)`.
  The `TenantId` type already exists, so this renames a property, a claim, a role and some columns, not a concept.
- The stores' keys are already `tenant_id`. "Firm" for the tenant survives in these places:
  - the claim `firm_id` (`PrincipalClaims.cs:9`);
  - the SQLite `FirmId` columns and their indexes (`MafDbContext.cs:34-53`, including the unique filtered
    `IX_AdminJobs_FirmId_Kind`);
  - JSON already stored: `principal.firmId` in `TurnTraces`, and `RunState.firmId` in Redis;
  - fixtures and tools:
    - `evals/*.jsonl` (`"firmId"` in every case);
    - `tools/Maf.Lab.A2AProbe/Scenarios.cs` and `tools/screenshots/capture.mjs`;
    - `compose/mcp-inspector/start.mjs` (`LAB_FIRM_ID`);
  - docs: `docs/http-api.md` (the `/dev/token` body, the `/api/me` shape), `docs/trace-events.md` and the README.
    `DECISIONS.md` is history and is not rewritten.

## Decisions

**Only the claim and the SQLite columns change (review, blocker 1).** The stores' keys are already `tenant_id`: the
Qdrant payload (`ChunkSchema.cs:6`) and every graph template (`GraphTemplates.cs:17,59`). "Firm" for the tenant
survives in two places only:
- the JWT claim `firm_id` (`PrincipalClaims.cs:9`);
- the `FirmId` columns of the api's SQLite tables (`MafDbContext.cs:34-53`).

The claim is renamed with a one-release alias. The columns are renamed in place by `DatabaseInitializer`.

**Order matters (re-audit 1).** `InitializeAsync` today runs `CREATE TABLE IF NOT EXISTS`, then the additive column
pass, then the indexes (`DatabaseInitializer.cs:37-50`). After the property is renamed, the additive pass would add an
empty `TenantId NOT NULL DEFAULT ''` before any rename. The rename would then fail as a duplicate, and every row's
tenant would be ''. So a **rename pass runs first**, before the additive pass:
- for each table that has `FirmId` and lacks `TenantId`, run `ALTER TABLE … RENAME COLUMN FirmId TO TenantId`;
- drop the old `IX_*_FirmId_*` indexes (including the unique filtered one on `AdminJobs`), so the index step creates
  the `TenantId` indexes in their place.

Each statement is applied only when its precondition holds, so the pass is idempotent.

**Stored JSON is read under both names.** The trace and run-state readers accept `firmId` as well as `tenantId` until
retention has aged out every old record: 7 days for traces, minutes for run state. A later cleanup drops the alias.

**The principal gains nothing ahead of time.** It becomes `Principal(UserId, TenantId, CoreRole)`. Group ids arrive
with `adopt-company-idp`, which needs them (YAGNI).

**The tenant filter stays in its one method.** `TenantScopedSearch` and `TenantScopedGraph.ReadAsync` change only the key
they bind. The query-path enumeration tests are unchanged apart from names.

**Domain words stay in the domain.** The billing domain's data, tools and prompts keep "firm". Only the core's tenant
changes name. mcp-retrieval's ledger keeps its `FirmId` column: it is part of the primary key of a raw-SQL table with
no migration path (`FeeAdjustmentLedger.cs:42-55`). Only the code that fills it changes, from `principal.FirmId` to
`principal.TenantId`.

**The rename pass is race-safe.** With `--scale api=2`, both replicas start at once. Each table's check and rename run
in one `BEGIN IMMEDIATE` transaction, so the second replica sees the renamed column and skips it. A "no such column"
from a lost race is tolerated, exactly as the additive pass tolerates a duplicate (`DatabaseInitializer.cs:98-101`).

## Risks / Trade-offs

- [A behaviour that relied on `Role.ADVISOR` in the core] → only the dev issuer, the eval principal and the web's types
  name it today. No retrieval is advisor-scoped yet, so the move has no isolation impact (confirmed by review).

- [Tokens issued before the rename] → the api accepts `firm_id` as an alias for one release, then drops it.

**Domain roles and attributes leave the core principal (decided with the user, 2026-10-05).** `ADVISOR`, `OPS` and
`AllowedAdvisorIds` are TAMP concepts. The core principal keeps only roles that any tenant has:
- `TENANT_ADMIN`;
- `USER`;
- `READ_ONLY`, which today's A2A and service principals already use (`BillingAgentHandler`, `AssistantBridge`,
  `ComplianceConsultant`).

`PLATFORM_ADMIN` follows with `adopt-company-idp`.

**Domain claims travel in the token.** The token carries domain roles and attributes as claims of their own:
`domain_roles` (for example `billing:advisor`, `billing:ops`) and `advisor_ids`. The core passes the token on and
never reads them. The domain's own MCP server (mcp-retrieval for billing) reads them, as it already validates the token
and derives the tenant itself, and applies them in its own tools.

**What moves:**
- `Principal` becomes `Principal(string UserId, TenantId TenantId, CoreRole Role)`. Group ids are added by
  `adopt-company-idp`.
- The dev issuer's personas keep their domain roles and advisor ids as claims, so the billing domain behaves exactly as
  before.
- `/api/me` stops returning advisor ids.

## Open Questions

None.
