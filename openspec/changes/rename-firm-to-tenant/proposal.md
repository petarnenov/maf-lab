# Proposal

## Why

The goal is a universal enterprise AI assistant (design 5q of `introduce-plugins`, decided with the user on
2026-10-05). It will be deployed as multi-tenant SaaS and as single-tenant installations from one codebase. Its core
must not speak one domain's language.

"Firm" is the TAMP billing domain's word, and today it is the core's word for a tenant:
- `firm_id` in tokens, payloads and graph nodes;
- `FirmId` on `Principal`;
- the `FIRM_ADMIN` role;
- the specs, CLAUDE.md and project.md.

It appears 471 times in 140 files. Renaming it now, before the plugin changes, touches far less code than renaming it
after them.

## What Changes

- **Code.**
  - `Principal.FirmId` becomes `TenantId` (the `TenantId` type already exists).
  - The `firm_id` claim becomes `tenant_id`.
  - `FIRM_ADMIN` becomes `TENANT_ADMIN`, with the `FirmAdmin` policy renamed to match.
  - Every core identifier, route doc and log field that means "tenant" follows.
- **Stored data.** Qdrant and Neo4j already use `tenant_id` (`ChunkSchema.cs:6`, `GraphTemplates.cs`), so nothing
  there changes.
  - The api's SQLite `FirmId` columns are renamed in place, together with their indexes, by a rename pass that runs
    **before** `DatabaseInitializer`'s additive pass.
  - Stored JSON (`principal.firmId` in traces, `RunState.firmId` in Redis) is read under both names until it ages out.
- **Fixtures, tools and docs.** `evals/*.jsonl`, the A2A probe, the screenshot script, the mcp-inspector start script,
  `docs/http-api.md`, `docs/trace-events.md` and the README follow.
- **Documents.** `tenant-isolation` and every spec that says "firm" for the tenant, plus `CLAUDE.md`, `openspec/project.md`
  and `.github/copilot-instructions.md`.
- **Domain roles leave the core.**
  - The core principal keeps `TENANT_ADMIN`, `USER` and `READ_ONLY`.
  - `ADVISOR`, `OPS` and `AllowedAdvisorIds` become domain claims (`domain_roles`, `advisor_ids`) that only the billing
    domain's MCP server reads.
  - Behaviour is unchanged: the dev personas carry the same domain claims.
- **Not renamed.** Domain language stays. The billing domain still speaks of firms, households, advisors and accounts in
  its own data, tools and prompts. Only the core's notion of a tenant changes name.
- **No behaviour change.** Tenant isolation works exactly as before, and the existing isolation tests pass unchanged
  apart from names.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `document-indexing`: "Uniform chunk metadata" says "tenant id" instead of "firm id".

- `tenant-isolation`: the principal carries a tenant id and one of the core roles (`TENANT_ADMIN`, `USER`,
  `READ_ONLY`). Domain roles and attributes are claims read only by the domain's server. Its wording uses "tenant"
  throughout.

## Principles

- SOLID: single responsibility at the level of language. The core names its own concepts (tenant, core roles), and the
  billing domain keeps its own (firm, advisor, ops) in its server.
- Standards: ubiquitous language and bounded contexts (Evans, *Domain-Driven Design*). "Tenant" and
  "tenant id" are the established terms of multi-tenant SaaS, also used by Keycloak and Entra ID.

## Progress

None — the only data change is a handful of SQLite column renames at api startup, each a metadata-only statement that
finishes in milliseconds; the rest is code.

## Stopping

None — the column renames are single SQLite statements run inside the api's startup, each atomic. No long work starts.

## Documentation impact

- `CLAUDE.md`: "Tenant (firm_id)" becomes "Tenant (tenant_id)".
- `openspec/project.md` and its generated copy in `openspec/config.yaml`: "Tenant is `firm_id`" becomes `tenant_id`.
- `.github/copilot-instructions.md`: the same.
- `docs/http-api.md`: role names in route descriptions.
- Every spec under `openspec/specs/` that means the tenant by "firm" is updated by this change's deltas, or at archive.
