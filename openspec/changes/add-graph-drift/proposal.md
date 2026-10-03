# Proposal

## Why

The billing corpus now lives in two stores: Qdrant holds its passages, and Neo4j holds a document node for every
billing document, with the mention edges that `trace_billing_relationships` follows. `make drift` checks only Qdrant
against the source. When the graph falls behind, for example after `make index-docs` without `make graph`, or after
a `make graph` that failed, nothing says so. The agent then answers relationship questions from a stale graph, and the
graph's document ids no longer lead to the passages `search_documents` returns. add-neo4j-graph left this as a
follow-up.

## What Changes

- **Document nodes record the content they were built from.** The billing graph builder adds `doc_hash` to each
  document node: the same source content hash Qdrant's chunks carry as `content_hash`. The node-level
  `content_hash` keeps its current meaning, which is the hash of the node's own properties used to skip unchanged
  writes. The first `make graph` after this change rewrites every document node once.
- **One more maintenance read.** `TenantScopedGraphMaintenance` gains a read that lists the billing document nodes
  with their tenant, document id and `doc_hash`. It runs one fixed Cypher text and takes no tenant parameter. The
  caller filters to its scope. No new graph read path is added, and no Cypher comes from input.
- **`DriftService` reports a graph section** next to the Qdrant one, measured against the same source documents in
  the same tenant scope:
  - `missingFromGraph` lists source documents with no document node.
  - `behind` lists document nodes whose `doc_hash` differs from the source's content hash. This includes nodes built
    before `doc_hash` existed.
  - `notInCorpus` lists document nodes with no source document left. `make graph` would remove them.
  - It also reports counts, an out-of-sync percentage of the source documents, and whether the graph was reachable.
- **Degrade, don't refuse.** When Neo4j cannot be reached, the graph section says `unavailable` with no hostname,
  query text or exception message. The Qdrant section and the exit code are unchanged.
- **`make drift` shows a progress bar** (progress-feedback) on stderr and keeps the JSON report on stdout:
  - an indeterminate bar while the corpus is read;
  - then the tenants listed from Qdrant out of the total, and a graph step;
  - one final line, for example `✓ drift: 624 documents — index 0 stale, graph 0 out of sync`, or `graph unavailable`
    when Neo4j cannot be reached.

  The command did not show a bar before this change.
- **`/admin/index` drift card** shows a second, compact line in the page's theme. It reads either "Graph: N of M out
  of sync" or "Graph: unavailable". The drift request is one Qdrant scroll per tenant plus one graph read, so it
  stays well under 3 seconds and needs no progress indicator. The card keeps its "Loading…" state.
- **API shape.** `DriftReport` gains an optional `graph` object, so existing fields and existing clients keep
  working.

Non-goals:
- The code graph compared with the code index (`maf_code_chunks`). That is a follow-up.
- Repairing drift. `make graph` and `make index` remain the fixes, and the report names which one to run.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `document-indexing`:
  - *Drift reporting* adds the graph section, compared against the same source documents.
  - The drift command shows its progress.
- `billing-graph`: *Billing graph model* stores each document node's source content hash.
- `graph-store`:
  - *One graph maintenance path* also lists the billing document nodes.
  - *One tenant-scoped graph read path* names the reads it covers (request, tool and model) and the maintenance reads
    allowed outside it. The current text says "every read", which the existing count and stale-removal reads already
    break.
- `web-ui`: *Index administration screen* shows the graph's drift next to the index's.

## Impact

- **Code:**
  - `src/Maf.Lab.Indexing/Graph/BillingGraphBuilder.cs` (`doc_hash`).
  - `src/Maf.Lab.Retrieval/Graph/TenantScopedGraphMaintenance.cs` (the list read).
  - `src/Maf.Lab.Indexing/Pipeline/DriftService.cs`.
  - `src/Maf.Lab.Indexing/Program.cs` (drift progress bar).
  - `src/Maf.Lab.Domain/Admin/AdminContracts.cs` (`GraphDrift`).
  - `web/src/api/types.ts`, `web/src/admin/IndexAdminPage.tsx`.
- **API:** `GET /api/admin/index/drift` returns an extra optional `graph` field. This is additive and not breaking.
- **Data:** the first `make graph` rewrites every billing document node once to add `doc_hash`. No volume reset is
  needed.
- **Dependencies:** none. The api and the indexer are already wired to Neo4j through `AddGraphStore`.
- **Tests:**
  - Unit tests for `DriftService` against substitutes: all four cases, and graph unavailable.
  - The builder sets `doc_hash`.
  - A Neo4j integration test for the list read.
  - A web test for the card's graph line.

## Documentation impact

- **README.md:** the `make drift` row in the generated make-targets block changes through the Makefile's `##` comment
  and `make docs`. The indexer usage line is unchanged.
- **docs/http-api.md:** the `/api/admin/index/drift` row names the `graph` section of `DriftReport`.
- **openspec/project.md:** the `Maf.Lab.Indexing` layout line already says "drift", so nothing changes.
- **CLAUDE.md** and **.github/copilot-instructions.md:** no rule or command changes.
