# Design

## Context

This is decision 5v, taken with the user on 2026-10-05 and moved here from `introduce-plugins`. It needs
`adopt-company-idp`, because group ids come from the IdP's principal.

### 5v. Document-level permissions, enforced in the core (decided with the user, 2026-10-05)

Tenant isolation is not enough for an enterprise: within a tenant, documents have permissions. The assistant SHALL
never retrieve, cite or summarise a chunk the asking user may not open at its source.

**Indexing.** Every chunk and every graph node carries an `acl`: the stable ids (never names) of the groups and users
allowed to read it at the source, or the explicit marker `tenant:all` for content open to the whole tenant.

**Fail closed.** The upsert path in `TenantScopedMaintenance` refuses a chunk with no `acl`, naming its source, so a
plugin that forgets permissions cannot index at all. A plugin's loader is the only place that reads permissions from its
source. The contract suite (3.7) indexes a fixture without an ACL and expects the refusal.

**Search.** The ACL filter is applied in the same one method that applies the tenant filter, `TenantScopedSearch`,
from the same principal: tenant, user id and group ids. So the rule "one method builds Qdrant queries" holds and no
plugin can skip the filter. `TenantScopedGraph.ReadAsync` binds the same ids into its fixed templates (see "The graph" below).

### The two markers, defined (re-review 4)

| Marker | Where | Means |
|---|---|---|
| tenant `shared` | `tenant_id` (unchanged) | the corpus every tenant may read, as today |
| `acl = ["tenant:all"]` | `acl` | every user of the item's tenant (for a `shared` item: every user of any tenant) |

So a `shared` item always has `acl = ["tenant:all"]`, and an item is readable when its tenant check passes **and**
its acl check passes.

### The graph (re-review 4, corrected by re-audit 5 and 6)

**Every node predicate, not only `all(…)`.** The graph templates restrict tenants in two forms:
- a path form, `all(x IN nodes(p) WHERE x.tenant_id IN $readable)` (`GraphTemplates.cs:17,59,71`);
- a per-node form, `x.tenant_id IN $readable` on each node, as in FirmRuns, SymbolCandidates, FileMethods and the
  RetrievalDense/Sparse templates.

The acl condition joins the tenant condition at every one of them: `any(a IN x.acl WHERE a IN $acl)` inside each
`all(…)`, and beside each per-node predicate. Here `$acl` is the principal's `user:<id>`, its `group:<id>`s and
`tenant:all`.

BillingNeighbourhood returns its start node `s` even when its OPTIONAL MATCH finds nothing, so `s` gets the condition
in its own WHERE, not only the optional part. The guard test (`GraphStoreTests.Every_template_guards_every_node_it_
matches`, through `CypherGuard`) is extended: every node must carry both the tenant and the acl predicate, and the
existing "unguarded template" cases gain acl-missing twins.

**Vector search in the graph.** The `SEARCH … WHERE` filter (`GraphTemplates.cs:108-110`; the filterable index properties are declared at
`TenantScopedGraphMaintenance.cs:74-79`) is checked for
whether Neo4j can push a list-membership condition on `acl` into the vector index.
- If it can, the condition goes there.
- If it cannot, the template over-fetches: it raises `LIMIT` stepwise up to a cap until k rows pass the acl filter.
  The cap is recorded, so a small tenant is never shortchanged silently. Which case holds is a task, verified against
  the real Neo4j image, not assumed.

**Which acl each node gets:**

| Node | Source | acl |
|---|---|---|
| `RetrievalChunk` | copied from a Qdrant point, **including its text** (`RetrievalCopyService.cs:100`) | **the point's own acl**, never `tenant:all`, or the copy would republish restricted text |
| document nodes | a document | the document's acl |
| `FeeSchedule` and other entities extracted from documents (`BillingGraphBuilder.cs:119-122`) | one or more documents | the **union** of those documents' acls. The node holds only identity fields (key, name). Edges carry no acl and nothing filters edges. This is safe because every edge derived from content touches a node carrying that content's acl, which the per-node check covers. So the union exposes only that the entity exists to someone who may read a document naming it. Rule: **an edge derived from content MUST touch a node that carries that content's acl** (a document node or a retrieval chunk). `Term -OccursIn-> RetrievalChunk` (`RetrievalCopyService.cs:113`) satisfies it through the chunk |
| `Term` | the BM25 vocabulary only, no document text | `tenant:all`, stated as such |
| `Firm`, `BillingRun`, `File`, `Method` | code or structured data, not a permissioned document | `tenant:all`, by construction |

The upsert refusal applies on the maintenance path, which every builder uses. So `make graph` cannot write a node
without an acl, and a builder that forgets fails its own run.

**Not in this change:** a live re-check at the source before citing (when permissions changed after indexing). That is
a later change, for customers who require it. Until then, permission changes reach the index on the source's next
sync, and that delay is stated in the operator's documentation.

**Tests.**
- An integration test against a real Qdrant indexes chunks with three ACLs and checks that each principal sees exactly
  its own. The same test runs for the graph.
- An architecture test keeps the ACL filter inside the one query method.

## Risks / Trade-offs

- [The ACL filter changes the one query method's inputs] → the filter's inputs (tenant, user, groups) all come from the
  `Principal`, so the method's signature does not gain a parameter a caller could set. The query-path enumeration test
  is unchanged in kind.
- [Full re-index] → existing corpora are migrated in place with `acl = [tenant:all]`, which is today's behaviour, by
  the migration tool. A real re-index is needed only for a source that has permissions.
