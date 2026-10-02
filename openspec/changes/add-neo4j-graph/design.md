# Design

## Context

See proposal.md for the motivation. The current state that shapes this design:

- **Vector-store tenancy.** All vector reads go through `TenantScopedSearch.QueryAsync(Principal, …)`, and the tenant
  condition comes only from `TenantFilter`. Writes go through `TenantScopedMaintenance`, keyed by `TenantId`.
  `QueryPathEnumerationTests` uses Mono.Cecil to prove that no other type touches Qdrant.
- **MCP tools.** Tools are `[McpServerTool]` methods. They get the principal from `IPrincipalAccessor` and return
  `CallToolResult` with structured DTOs. Failures go through `ToolErrors`.
  - `mcp-retrieval` hosts the billing tools.
  - `mcp-code` (`Maf.Lab.CodeSearch`) hosts `search_codebase` and `ask_codebase` over a shared corpus.
- **Indexing.** `Maf.Lab.Indexing` is the one indexing CLI. It draws progress with `ConsoleProgress`, and make runs
  it once per domain using env overrides. `scripts/index_if_empty.sh` indexes a collection only when it is empty.
- **Billing data.**
  - Seed: `compose/seed/{billing-accounts,billing-runs,portfolio-households}.json`. Account ids are shared between
    billing and portfolio. The `note` fields hold injection and canary text on purpose.
  - Corpus: `data/{firm-*,shared}/…`. The tenant is taken from the first path segment.
- **Code.** The code chunker is regex-based. Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.9.0) is already pinned and used
  by TestGen and CoverageRunner.
- **Health and topology.** Neither uses ASP.NET health checks. Topology health comes from probes in `TopologyProbe`,
  and `TopologyTests` keeps `docs/topology.drawio` in step with its node ids.

## Goals / Non-Goals

**Goals:**
- A graph read path whose tenant isolation is proven structurally, the same way the Qdrant path is.
- Graph data that links back to the vector store, through the same `doc_id` and the same line spans, so the agent can
  move from a relationship to a passage.
- A build that is deterministic and idempotent, with no model in the loop.

**Non-Goals:**
- **Jev routing for the graph tools.** `intent-classification` routes data questions to named read tools. Adding the
  graph tools to that `Choice` changes the Jev request, which brings the §7 review, labelled sets and threshold work.
  That is a follow-up change. Until then the model picks graph tools from their descriptions, and selection evals
  measure how well. The rule that forces `search_codebase` for codebase turns still applies, so a structural code
  question first gets a search and can then call `trace_code_symbol`.
- **Model-generated Cypher (text-to-Cypher).** It is rejected for tenancy and injection reasons, as explained below.
- **Entity extraction by an LLM, and graph embeddings or vector indexes inside Neo4j.**
- **A portfolio subgraph.** Households come from the portfolio seed, but only as billing context. `mcp-portfolio`
  gets no graph tool.
- **Code graphs for TS, Python or SQL.** The code graph covers C# only.

## Decisions

### D1. Neo4j Community, one database, tenancy as a property
Neo4j Community has one user database. Every node carries `tenant_id` (a firm id or `shared`), with an index on
`(label, tenant_id)` and uniqueness constraints on each label's stable key, scoped by tenant.
- *Alternative:* a database per tenant (Enterprise only). Rejected because of the licence, and because it diverges
  from the single-collection Qdrant model that the lab is designed to exercise.
- *Alternative:* a separate graph per subgraph. Rejected because one instance with labelled subgraphs is enough.
  Billing nodes use labels `Firm`, `Household`, `Account`, `BillingRun`, `FeeSchedule` and `Document`. Code nodes use `Project`,
  `File`, `Type` and `Method`. No query crosses the two subgraphs.

### D2. Closed set of Cypher templates and one read method
The read path is `TenantScopedGraph.ReadAsync<TResult>(Principal principal, GraphQuery<TResult> query, CancellationToken ct)`.
- `GraphQuery<T>` is a sealed hierarchy of named records, one per query: `BillingNeighbourhood(entityId, depth)`,
  `SymbolTrace(name, direction, depth)` and `FileImpact(path, depth)`.
- Each record carries its constant Cypher text, typed parameters, a depth cap and a node limit.
- `ReadAsync` adds `$readable = principal.ReadableTenants` and runs the query in a read transaction.
- Every node pattern in a template must be guarded by `n.tenant_id IN $readable`. Variable-length paths are guarded
  with `all(n IN nodes(p) WHERE n.tenant_id IN $readable)`.
- Depth cannot be a Cypher parameter in a variable-length pattern. So each template has one constant text per allowed
  depth: 1–2 for billing and 1–3 for code. The text is chosen by an enum value, never by string concatenation.

How this is enforced:
- A unit test parses every template, using a small Cypher pattern scan over its constant text. It fails if any node
  variable lacks the guard.
- `QueryPathEnumerationTests` is extended so that only `TenantScopedGraph` and `TenantScopedGraphMaintenance` may
  reference `Neo4j.Driver.IDriver`, `IAsyncSession` or `IAsyncQueryRunner`. A rogue fixture proves that the check
  detects violations.

Alternatives considered:
- *Text-to-Cypher by the model.* Rejected. It would make the model a query author: the tenant guard would have to be
  injected into arbitrary Cypher, and prompt injection would reach the database.
- *Driver-level filtering.* Rejected. Neo4j has no per-query row filter in Community.

This deliberately does **not** touch `TenantScopedSearch`. The CLAUDE.md rule ("one method builds Qdrant queries")
stays true, and it gains a graph counterpart.

### D3. Where the code lives
- **`src/Maf.Lab.Retrieval/Graph/`** holds `TenantScopedGraph`, `TenantScopedGraphMaintenance`, the `GraphQuery`
  records, `GraphOptions` (`Neo4j:Uri`, `Neo4j:User`, `Neo4j:Password`, `Neo4j:Database`) and `AddGraphStore()`.
  - Retrieval already holds the store access that `CodeSearch` and `Indexing` reuse, so the graph code goes in the
    same place and no new project is needed.
- **`src/Maf.Lab.Domain/Graph/`** holds the tool DTOs: `BillingRelationships`, `CodeTrace` and `ChangeImpact`.
- **Tools:**
  - `src/Maf.Lab.Retrieval/Tools/BillingGraphTools.cs`.
  - `src/Maf.Lab.CodeSearch/Tools/CodeGraphTools.cs`.
  - Both follow the existing pattern: `ReadOnly=true`, `Idempotent=true`, `OpenWorld=false`, structured content, and
    `ToolErrors.ForException("Graph lookup", …)`.

### D4. Build: an indexer `graph` command with two builders
`Maf.Lab.Indexing graph [--only billing|code]` runs `BillingGraphBuilder` and `CodeGraphBuilder`. Both emit
`GraphWrite` batches to `TenantScopedGraphMaintenance`.

Idempotency:
- Each run gets a `run_id`. Nodes and edges are written with `MERGE` on `(label, tenant_id, key)`, and `run_id` and
  `content_hash` are set on each.
- After a source finishes, its nodes from older runs are removed with `DETACH DELETE` on
  `source = $source AND run_id <> $run`.
- Unchanged nodes count as "unchanged" when their hash matches. The run still stamps them with the new `run_id`, but
  reports them as unchanged rather than written.

**Billing builder:**
- Reads the three seed files, without ever reading `note`.
- Reads the billing corpus through the existing `CorpusLoader`, so document tenancy and `doc_id` are identical to
  Qdrant's.
- Finds mentions by matching known account and household ids from the seed against document text, using a
  whole-word match.
- Finds fee schedules with one fixed pattern, `\b[A-Z]{2,6}(-[A-Z0-9]{2,6}){1,3}-20\d{2}-\d{3}\b`, over document text.
  Each distinct code becomes a `FeeSchedule` keyed by the code under the document's tenant, with a `MENTIONS` edge
  from every document that contains it.
  - The pattern requires a year and a three-digit serial at the end, so internal reference codes such as
    `NW-CANARY-7731-HH0005` and `ACME-CANARY-4410` do not match.
  - *Why:* while implementing it turned out that no corpus document contains a seed account or household id. Firm B's
    household profiles and fee-schedule notes do share schedule codes (about 120 of them), which gives the billing
    graph real edges. The seed-id mentions stay in place for when the corpus does name accounts.

**Code builder:**
- Uses `CSharpCompilation` per `.csproj`. The sources are the git-tracked `.cs` files under each project folder. The
  references are the runtime's trusted platform assemblies plus the other repository projects' compilations, as
  `CompilationReference`s.
- A `CSharpSyntaxWalker` with the `SemanticModel` resolves invocations. Only targets whose `DeclaringSyntaxReferences`
  are in the repository produce `CALLS` edges.
- NuGet assemblies are not restored, so calls into them do not resolve. They are counted as unresolved and dropped,
  which matches the spec, where external calls produce no nodes.
- Method keys are `DocumentationCommentId`s, which are stable and unique per overload.
- *Alternative:* `MSBuildWorkspace`. Rejected because it needs MSBuild and a NuGet restore inside the indexer
  container, which makes the build slower and more fragile.

Progress:
- An `IndexProgressBar`-style `GraphProgressBar` over `ConsoleProgress`, with phases `billing` and `code`.
- The total is the number of seed records plus documents, and then the number of files.
- stdout carries the JSON summary, which is: written, unchanged, removed, rejected and unresolved calls.

Make:
- `graph` runs `$(INDEXER) graph` under `HOST_ENV` and depends on `infra` (extended with `neo4j`).
- `index` runs `graph` last.
- `scripts/index_if_empty.sh` checks `MATCH (n) RETURN count(n) LIMIT 1` through `cypher-shell` in the neo4j
  container, and runs the graph build when the count is 0.

### D5. Compose and the inspector
```yaml
neo4j:
  image: neo4j:<pinned>-community
  environment:
    NEO4J_AUTH: neo4j/${NEO4J_PASSWORD:-maf-lab-dev}
    NEO4J_server_memory_heap_max__size: 512m
    NEO4J_server_bolt_advertised__address: localhost:7687
  ports: ["127.0.0.1:7687:7687"]
  volumes: [neo4j-data:/data]
  healthcheck: wget -qO- http://127.0.0.1:7474 (interval 5s, retries 30, start_period 20s)
```
- Bolt is published on loopback only. The indexer runs on the host (`make graph` under `HOST_ENV`, as `make index`
  reaches Qdrant on 6334), so it needs Bolt there; nothing listens on other interfaces.
- `x-app-env` gains `Neo4j__Uri: bolt://neo4j:7687`, `Neo4j__User: neo4j` and `Neo4j__Password: ${NEO4J_PASSWORD:-maf-lab-dev}`.
- `mcp-retrieval` and `mcp-code` gain `depends_on: neo4j: service_healthy`, and `lb` waits for neo4j.
- Neo4j Browser is a page served by 7474 that opens Bolt from the user's browser at the advertised
  `localhost:7687`. A dev-only `neo4j-browser` service (profile `inspectors`, a pinned `alpine/socat` image) forwards
  `127.0.0.1:7175 → neo4j:7474`, so with `CI_MODE=1` no Browser port is published.
  - *Alternative:* publish 7474 on `neo4j` directly. Rejected because ports cannot be profile-gated per service.
- No nginx location is added. Graph access is service-to-service, and the browser is an inspector like Redis Insight,
  outside the balancer. `lb-routes` is therefore unchanged.

### D6. Health, topology and telemetry
- **Topology:**
  - `TopologyProbe` gains node `neo4j` ("Graph store") and edges `mcp-retrieval → neo4j` and `mcp-code → neo4j`.
  - Its probe calls `IDriver.VerifyConnectivityAsync`, which reports healthy or unreachable.
  - The node is added to `docs/topology.drawio` without overlapping boxes, as `TopologyTests` requires.
- **Startup:** the MCP servers do not refuse to start without Neo4j (no `Require…` hosted service). Graph tools
  degrade to the safe error, as the spec requires.
- **Telemetry:**
  - `TenantScopedGraph` starts the activity `graph.read` with the tags `graph.query` (template name),
    `graph.rows`, `graph.truncated` and `graph.duration_ms`. Maintenance starts `graph.write` with counts.
  - The histogram is `maf_graph_query_duration_seconds{query}`.
  - None of these carry parameter values.

### D7. Pins (DECISIONS.md §75)
- The `neo4j` image, the latest stable `-community` tag at implementation, used in both compose and Testcontainers.
- `Neo4j.Driver` and `Testcontainers.Neo4j`, at the same Testcontainers line as `Testcontainers.Qdrant`, which is
  4.15.0.
- `alpine/socat`.

All exact versions are recorded in §75 in the same commit that adds them.

## Risks / Trade-offs

- [The regex template guard check misses a pattern form] → Templates are few and reviewed. The scan is conservative:
  any node pattern `(x…)` without a guard fails the check. The integration leakage test (a firm B node bridged
  through a shared node) is the behavioural backstop.
- [Roslyn without NuGet references leaves many calls unresolved, for example extension methods from packages] → Only
  repository-internal edges are needed. The unresolved count is reported in the summary. If calls *between*
  repository projects fail to resolve, the project compilations are referenced in topological order of
  `ProjectReference`.
- [Neo4j memory in the dev stack] → The heap is capped at 512m and the page cache at 256m. The corpus and the repo are
  small.
- [Selection regressions from three new tools] → The descriptions name their siblings. Selection evals run before
  merge, and the baseline is updated only after the run is accepted.
- [The graph and the vector store drift apart, for example a document re-indexed in Qdrant but not in the graph] →
  `make index` always ends with `graph`. Document nodes carry `content_hash`. `make drift` reports
  graph-vs-Qdrant `doc_id` mismatches as an extra section (read-only).
- [The forced `search_codebase` precedes graph tools on codebase turns] → This is accepted for now. Jev routing for
  graph tools is the named follow-up.

## Migration Plan

This is purely additive.
- **Rollout:** after `git pull`, `make` builds the new service, and `index-if-empty` builds the graph.
- **Rollback:** revert the change, then remove the volume with `docker volume rm maf-lab_neo4j-data`. No Qdrant data
  or schema changes.

## Open Questions

- The exact Neo4j image tag and the driver version. These are picked at implementation as the latest stable, and do
  not affect the specs.
- Whether `docs/telemetry.md` enumerates span names. If it does, the new spans are added there.
