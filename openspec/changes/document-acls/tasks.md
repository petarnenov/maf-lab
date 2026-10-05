# Tasks

No decision-engine call is added or changed, so the Jev review checklist does not apply.

- [ ] 1.1 `acl` on every chunk and graph node, with a Qdrant payload index on it. `TenantScopedMaintenance` refuses an
      upsert without it.
- [ ] 1.2 `TenantScopedSearch` and `TenantScopedGraph.ReadAsync` filter by tenant, user and groups from the principal.
      Verify with Testcontainers (Qdrant and Neo4j) that three principals each see exactly their chunks, and with the
      enumeration test that the filter lives only in those methods.
- [ ] 1.25 The graph:
  - add the acl condition at every node predicate (path `all(…)` and per-node forms), including BillingNeighbourhood's
    start node;
  - extend `CypherGuard` and its test with acl-missing cases;
  - `RetrievalCopyService` copies each point's acl;
  - extracted entities get the union of their documents' acls and hold identity fields only;
  - `Term` and code-built nodes get `tenant:all`;
  - a guard test fails a builder that creates a content-derived edge touching no node that carries that content's acl
    (a document node or a retrieval chunk), and passes `Term -OccursIn-> RetrievalChunk`;
  - check, against the real Neo4j image, whether `SEARCH … WHERE` can filter on `acl`, else over-fetch up to a
    recorded cap.

  Verify the scenarios "A path through a restricted node", "make graph sets permissions by construction" and
  "Restricted chunk text is not republished by the graph".
- [ ] 1.3 A new target, `make acl-backfill` (not the embedding `make migrate`), and a new route,
      `POST /api/admin/index/acl-backfill` with job kind `acl-backfill`, migrate existing points and nodes to
      `tenant:all`, with progress and Ctrl+C as the proposal says, and from
      the page with Esc through the admin job's cancel route. Verify that
      the isolation suite and `make eval SUITE=retrieval` are unchanged.
- [ ] 1.4 Add the case "indexed without an acl is refused" to the plugin contract suite.
