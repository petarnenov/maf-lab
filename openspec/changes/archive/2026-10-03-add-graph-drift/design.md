# Design

## Context

- `DriftService` (in `Maf.Lab.Indexing`) loads the billing corpus for a tenant scope. For each tenant it lists the
  indexed documents through `TenantScopedMaintenance.ListDocumentsAsync`, which aggregates one Qdrant scroll. It
  returns `DriftReport`. Two callers use it:
  - the indexer's `drift` command, with no principal, all layout tenants or `--tenant`;
  - `GET /api/admin/index/drift`, scoped to the principal's readable tenants.
- **The source content hash.** `SourceDocument.ContentHash` is the first 16 hex characters of the SHA-256 of the file
  content, and every Qdrant chunk stores it as `content_hash`. The billing graph's document node also has a
  `content_hash`, but `TenantScopedGraphMaintenance` sets it to a hash of the node's own properties, so a rebuild can
  skip unchanged nodes. Today the two values are unrelated. Observed: `firm-a/code/AcmeTierFeeCalculator.cs` is
  `20be67a51b5e3ba1` in Qdrant and `6cd5134a458f0d4d` on its node.
- **The graph's readers.** `TenantScopedGraph.ReadAsync` serves tools. `TenantScopedGraphMaintenance` writes, and
  already reads counts and stale nodes with fixed Cypher. `GraphStoreTests` uses Cecil to check that only those two
  types open a Neo4j session.
- **Wiring.** The api and the indexer already register the graph store (`AddGraphStore`), and `make drift` depends on
  `make infra`, which starts Neo4j.
- **Progress.** The `drift` command has no progress bar today, which `progress-feedback` requires. `index` and
  `graph` use `ConsoleProgress` on stderr.

## Goals / Non-Goals

**Goals:**
- One `drift` call answers both questions, "is the index behind the source?" and "is the graph behind the source?",
  for the same documents and the same tenants.
- The graph check works on a graph built before this change and reports it as behind, not as an error.

**Non-Goals:**
- Comparing the code graph with `maf_code_chunks`.
- A separate graph-vs-Qdrant comparison. Both are compared against the source, which names the side to rebuild.
- Changing what the Qdrant half reports.

## Decisions

1. **The graph is compared against the source.** The other options were Qdrant as the reference, or a three-way
   matrix. With the source as the reference, each side reports what it lacks, with the same meaning and the same
   denominator (the source documents in scope). A difference between graph and Qdrant then follows from the two
   lists, and the fix is obvious: `make graph` for the graph list, and `make index` for the index list.
2. **A new node property `doc_hash`.** It is set by `BillingGraphBuilder` to `SourceDocument.ContentHash` and sits
   next to the existing node-level `content_hash`.
   - *Rejected:* redefining `content_hash` as the source hash. The maintenance code depends on it to detect unchanged
     nodes, and a title or path change would then go undetected.
   - *Rejected:* comparing timestamps. Nodes carry no `updated_at`, and timestamps miss reverts and clock skew.
   - Adding the property changes the node's own hash, so the first build after deploy rewrites every billing document
     node once. That is about 624 writes, and later builds are unchanged.
3. **`TenantScopedGraphMaintenance.ListDocumentsAsync(string source, CancellationToken)`.** It runs one fixed text:
   `MATCH (d:Document) WHERE d.source = $source RETURN d.tenant_id, d.key, d.doc_hash`, ordered by key. It returns
   `IReadOnlyList<GraphDocument(TenantId Tenant, string DocId, string? DocHash)>`.
   - There is no tenant parameter. `DriftService` filters by its scope, the same way it builds the Qdrant half, so
     the admin endpoint never reports another firm's ids.
   - *Rejected:* a named query on `TenantScopedGraph.ReadAsync`. Drift runs in the indexer without a principal, and
     the read-path queries are bounded neighbourhood lookups built for tools. A listing of every document node does
     not fit that contract. The graph-store delta states this boundary explicitly.
4. **The report shape is additive.** `DriftReport` gains `GraphDrift? Graph`:
   - `Available` (bool) and `Reason` (`"unreachable"` or null);
   - `MissingFromGraph`, `Behind` and `NotInCorpus` (each a list of doc ids);
   - `OutOfSync` (the count of source documents missing or behind) and `OutOfSyncPercent`.

   `NotInCorpus` is listed but not counted in the percentage, because its denominator is the source. The web type
   mirrors the C# type. `Graph` is null only when the service runs without a graph store registered, which never
   happens in the compose stack.
5. **Degrading.** `DriftService` catches the driver's connectivity and `ServiceUnavailableException` errors around the
   one graph read and returns `Available=false, Reason="unreachable"`. It logs the exception type only, and never the
   message, the URI or the query (graph logs carry structure only). It catches nothing else, so a real bug still
   fails.
6. **Progress for `drift`.** The command uses `ConsoleProgress("drift")` on stderr:
   - `Step("reading corpus")` (indeterminate);
   - `SetTotal(tenants)` and `Advance()` per tenant listed from Qdrant;
   - `Step("reading graph")`;
   - `Succeed("{n} documents: index {stale} stale, graph {outOfSync} out of sync")`, with
     `graph unavailable` in place of the graph count when it is unavailable.

   `DriftService.ComputeAsync` takes an optional `IProgress<IndexProgress>`. The api passes none. The JSON on stdout is
   unchanged except for the new field.
7. **Web.** The drift card in `IndexAdminPage` adds one muted line under "x of y documents": "Graph: {outOfSync} of
   {total} out of sync" or "Graph: unavailable", with no new component and no new style. Measured during apply: the
   endpoint on the dev stack must stay under 3 s. If it does not, the card gets the page's existing themed progress
   treatment (task 5.3).
8. **Guarding the boundary.** `GraphStoreTests` gets a Cecil check: no type in `Maf.Lab.Retrieval.Tools`,
   `Maf.Lab.CodeSearch.Tools` or `Maf.Lab.Api.Agent` calls `TenantScopedGraphMaintenance` (the request path never
   reaches a maintenance read).

## Risks / Trade-offs

- [After deploy, every document reads as behind until the next `make graph`] → This is the intended signal. The
  final drift line and DECISIONS say to run `make graph`. `make` (index-if-empty) does not rebuild a non-empty graph,
  so the first check after pulling this change shows it once.
- [A large graph listing] → There are about 624 billing document nodes. Each row is three short strings, so the
  listing is far below any memory concern. The maintenance read is not bounded by a node limit, unlike tool queries.
  That is deliberate, and the spec says so.
- [Corpus-reading cost in the api] → `DriftService` already reads the corpus in the api today, so nothing new.

## Migration Plan

1. Deploy, then run `make graph` once. It writes `doc_hash` on every document node.
2. `make drift` then reports the graph at 0 out of sync.

Rollback: revert. The extra `doc_hash` property is ignored by older code, and the next build of the old code leaves it
in place harmlessly.
