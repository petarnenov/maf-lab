# Proposal

## Why

Tenant isolation keeps one tenant's documents from another. Within a tenant, though, documents have permissions:
- HR policies are for everyone;
- payroll is for HR only;
- a customer contract is for its team.

An assistant that retrieves, cites or summarises a document the asking user may not open at its source leaks it. This
is the most common failure of enterprise RAG (decision 5v, with the user, 2026-10-05).

## What Changes

- **Permissions in the index.** Every chunk and graph node carries an `acl`: the ids of the groups and users allowed
  at the source, or `tenant:all`.
- **Fail closed.** An upsert without an `acl` is refused, naming its source.
- **One filter.** The ACL filter is applied in the same one method as the tenant filter (`TenantScopedSearch`,
  `TenantScopedGraph.ReadAsync`), from the principal alone.
- **Existing corpora** are migrated in place to `tenant:all`, so today's behaviour is unchanged.
- **Plugin contract.** A plugin's loader supplies the permissions. The contract suite indexes a fixture without an
  `acl` and expects the refusal.

## Capabilities

### New Capabilities

- `document-permissions`: permissions on every indexed item, fail-closed indexing, and filtering by the principal in
  the one query method.

### Modified Capabilities

- `document-indexing`: "Uniform chunk metadata" gains `acl`. `tenant-isolation` is unchanged: document permissions
  narrow within a tenant, and its own requirements still hold as written.

## Principles

- SOLID: the permission filter joins the tenant filter in the one query method (single responsibility for "who may
  read"). Loaders only supply data.
- Standards:
  - Security: security trimming, also called permission-aware retrieval, as in SharePoint search and Azure AI Search
    security filters; fail-closed defaults; least privilege.
  - Directory and patterns: group ids from the IdP (SCIM-provisioned groups); Qdrant payload filtering with a payload
    index.

## Progress

- Terminal: the in-place migration of existing points and nodes to `tenant:all` shows the migration tool's bar
  (done/total per collection).
- Page: the same backfill started from the index administration screen (`POST /api/admin/index/acl-backfill`, its
  own route and job kind `acl-backfill`, never the embedding migration's `/index/migrate`) shows the
  screen's themed progress from its admin job (re-review 3).

## Stopping

- Key: Ctrl+C or SIGTERM during the migration; Esc on the index administration screen while it runs
- Stop: between batches. Each point or node gets its `acl` atomically, and a rerun skips those that have one. In the
  terminal it exits 130. From the page, Esc calls `POST /api/admin/jobs/{id}/cancel`: the job row turns `canceled`
  atomically, and the worker on any replica watches that row and stops at the next batch.
- Recorded in: the data itself (a point has an `acl` or does not), and for a page run the admin job's row
- Shown: in the terminal, "Stopped — <n> of <total> have permissions; run `make acl-backfill` again to finish". On the page,
  "Stopping…" until the job row says canceled, then the count reached

## Documentation impact

- CLAUDE.md and project.md: the "one method builds Qdrant queries" rule now names the permission filter too.
- `docs/plugins.md`: loaders must supply permissions.
- `docs/http-api.md`: `POST /api/admin/index/acl-backfill`. README: the generated `make-targets` block gains
  `acl-backfill` (`make docs`). docs-check enforces both.
- DECISIONS §85 (new, written when this change is applied): groups from the token only, staleness bounded by the
  access-token lifetime; the two markers.
