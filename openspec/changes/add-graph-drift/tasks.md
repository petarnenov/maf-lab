# Tasks

## 1. Graph: record and list the source hash

- [x] 1.1 In `BillingGraphBuilder`, add `doc_hash` = `SourceDocument.ContentHash` to every document node. Verify: a
  builder unit test asserts the node's `doc_hash` equals the document's `ContentHash`, and that it changes when the
  content changes.
- [x] 1.2 Add `TenantScopedGraphMaintenance.ListDocumentsAsync(source, ct)`, which runs one fixed Cypher text and
  returns `GraphDocument(Tenant, DocId, DocHash?)` with no title, path or text. Verify: a Neo4j integration test
  writes document nodes for two tenants and gets both back with their hashes, and a node without `doc_hash` comes
  back with a null hash.
- [x] 1.3 Add a Cecil check to `GraphStoreTests`: no type in `Maf.Lab.Retrieval.Tools`, `Maf.Lab.CodeSearch.Tools` or
  `Maf.Lab.Api.Agent` calls `TenantScopedGraphMaintenance`. Verify: the test passes, and it fails when a tool type is
  made to call `ListDocumentsAsync` (try it locally, then revert).

## 2. Drift report

- [x] 2.1 Add `GraphDrift` (`Available`, `Reason`, `MissingFromGraph`, `Behind`, `NotInCorpus`, `OutOfSync`,
  `OutOfSyncPercent`) and an optional `Graph` field to `DriftReport` in `AdminContracts.cs`. Verify: the solution
  builds, and the existing drift tests still pass.
- [x] 2.2 In `DriftService.ComputeAsync`, compute the graph section against the same source documents, filtered to
  the same tenant scope. Take an optional `IProgress<IndexProgress>`. Return `Available=false, Reason="unreachable"`
  on driver connectivity or service-unavailable errors, and log the exception type only. Verify with unit tests on
  substitutes for:
  - in sync (0%);
  - behind (hash differs, and hash null);
  - missing from the graph;
  - no longer in the corpus (listed, not counted);
  - another tenant's nodes filtered out;
  - graph unreachable (Qdrant half unchanged).

## 3. CLI progress

- [x] 3.1 Wrap the indexer's `drift` command in `ConsoleProgress("drift")` on stderr: reading corpus, then tenants
  listed out of the total, then reading graph, ending in the summary line from the spec. Keep stdout as the JSON
  only. Verify:
  - `make drift` in a terminal shows the bar and the final line;
  - `make drift > out.json` gives valid JSON with `graph`, and stderr holds plain lines only;
  - a `ConsoleProgress`-based test, or the existing pattern used for `graph`, covers the summary text.
- [x] 3.2 Update the Makefile's `## drift` comment to name both checks, for example "Report stale documents: index
  and graph vs source". Verify: `make help` shows it.

## 4. Web

- [x] 4.1 Add `GraphDrift` and the optional `graph` field to `DriftReport` in `web/src/api/types.ts`.
- [x] 4.2 In `IndexAdminPage`, add one muted line to the drift card: "Graph: {outOfSync} of {total} out of sync", or
  "Graph: unavailable". Verify with Vitest that both texts render from mocked responses, and that the card shows no
  error state when the graph is unavailable.
- [x] 4.3 Time `GET /api/admin/index/drift` on the running stack as FIRM_ADMIN. Verify it stays under 3 s. If it does
  not, add the page's themed progress to the card per `progress-feedback`.
  - Measured: 0.09–0.57 s with Neo4j up; about 8 s with Neo4j down (its connect timeout), so the card shows the
    page's `Progress` while it loads.

## 5. End to end

- [x] 5.1 On the running stack:
  - Run `make graph` once, which adds `doc_hash`. Verify `make drift` reports the graph at 0 out of sync.
  - Touch one billing document's text and run only the indexer's `index` command (no graph build). Verify the document is listed under graph
    `behind` and not under index `stale`.
  - Run `make graph`. Verify it is back to 0.
  - Restore the document.
- [x] 5.2 Stop `neo4j` and run `make drift`. Verify the graph section is `unavailable`, the final line says so, the
  exit code is 0, and stderr carries no hostname or stack trace. Start `neo4j` again.
- [x] 5.3 Open `/admin/index` as a firm admin in the browser. Verify the graph line in the page's theme in light and
  dark, with no other firm's ids in the response.
- [x] 5.4 Run `make test`, `make lint`, `make verify` and `make specs` (`openspec validate --all --strict`). Verify all
  pass.
  - 2026-10-03, after merging add-graph-trace-event:
    - `make test`: 1505/1506 .NET. The one failure, `RunProtocolTests.A_run_stopped_by_its_client_ends_and_nothing_runs_after`
      (a client-abort race, IOException instead of OperationCanceledException), passes when its class runs alone (10/10).
    - Web: 616/616.
    - `make lint`, `make verify` (37 + 8), `make specs` (47) and `make docs-check` pass.
    - Found live: indexer warnings went to stdout and broke the JSON, so console logs now go to stderr.

## 6. Documentation

- [x] 6.1 In `docs/http-api.md`, give the `/api/admin/index/drift` row the `DriftReport` graph section. Run
  `make docs` so the README's generated make-targets row picks up the new `## drift` comment (never edit inside the
  `generated:` block by hand).
- [x] 6.2 Add a DECISIONS.md entry (graph drift against the source, `doc_hash` next to the node `content_hash`, the
  maintenance read outside the read path). Run `make docs-check`. Verify it passes.
