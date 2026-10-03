# Tasks

No Jev call is added or changed, so the Jev review checklist (docs/rules/jev-usage.md §7) does not apply. Jev routing
for the graph tools is a named follow-up (design.md, Non-Goals). The Bulgarian labelled inputs are still covered, by
the selection cases in 9.1.

## 1. Pins and packages

- [x] 1.1 Pick the latest stable `neo4j:<x>-community` image, `Neo4j.Driver`, `Testcontainers.Neo4j` (same line as
  `Testcontainers.Qdrant`) and `alpine/socat`. Add them to `Directory.Packages.props` and reference the packages from
  Retrieval, Indexing and IntegrationTests. Verify `dotnet restore` and `dotnet build` succeed.
- [x] 1.2 Write DECISIONS.md §75 (add-neo4j-graph) with the pins table rows and the decisions D1–D7. It goes in the
  same commit as 1.1. Verify that `grep -n "## 75" DECISIONS.md` finds it.

## 2. Compose

- [x] 2.1 Add the `neo4j` service as in design D5 (pinned image, `NEO4J_AUTH` from `NEO4J_PASSWORD`, a heap and page
  cache cap, `neo4j-data` volume, a healthcheck with `start_period`, Bolt published on `127.0.0.1:7687` only), and declare the
  volume. Verify that `make up` reports it healthy and `docker port` lists only the loopback Bolt port.
- [x] 2.2 Add `Neo4j__Uri`, `Neo4j__User` and `Neo4j__Password` to `x-app-env`. Add `depends_on: neo4j:
  service_healthy` to `mcp-retrieval`, `mcp-code` and `lb`. Verify with `docker compose config` that the start order is
  neo4j, then the MCP servers.
- [x] 2.3 Add the `neo4j-browser` inspector (profile `inspectors`, `alpine/socat`, `127.0.0.1:7175 → neo4j:7474`, a
  healthcheck, `depends_on: neo4j healthy`). Verify that
  `http://localhost:7175` opens Neo4j Browser and connects, and that with `CI_MODE=1` `make ps` shows no such container.

## 3. Graph store access (Retrieval)

- [x] 3.1 Add `Graph/GraphOptions` and `AddGraphStore()`, which register an `IDriver` singleton. Verify that a unit test
  binds the options from configuration and that the password never appears in `ToString()` or in logs.
- [x] 3.2 Add the `GraphQuery<T>` sealed records `BillingNeighbourhood`, `SymbolTrace` and `FileImpact`, with constant
  Cypher per allowed depth, typed parameters, a depth cap and a node limit. Verify with unit tests that out-of-range
  depth is rejected and that no template is built by concatenation.
- [x] 3.3 Implement `TenantScopedGraph.ReadAsync(Principal, GraphQuery<T>, ct)`. It binds `$readable` from
  `principal.ReadableTenants`, runs a read transaction, applies the limit and sets truncated, and emits the
  `graph.read` span and `maf.graph.query.duration`. Verify with a unit test that the parameters always contain
  `readable` and that no other parameter can be named `readable`.
- [x] 3.4 Implement `TenantScopedGraphMaintenance`: create the constraints and indexes, batched `MERGE` writes keyed by
  `(label, tenant_id, key)` with `run_id` and `content_hash`, stale removal per source, and rejection of nodes without
  a tenant. Verify the rejection and batching with unit tests.
- [x] 3.5 Add the template guard test, which fails if any node variable in any template lacks
  `tenant_id IN $readable`. Add a deliberately unguarded fixture template and verify the test catches it.
- [x] 3.6 Extend `QueryPathEnumerationTests` so that only `TenantScopedGraph` and `TenantScopedGraphMaintenance`
  reference `IDriver`, `IAsyncSession` or `IAsyncQueryRunner`. Add a rogue graph fixture. Verify the suite passes, and
  that it fails with the fixture enabled.

## 4. Graph build (Indexing)

- [x] 4.1 Implement `BillingGraphBuilder`. It reads the three seed files without `note`, reads the documents through
  `CorpusLoader`, finds mention edges by whole-word id match, and extracts `FeeSchedule` nodes with the fixed code
  pattern under the document's tenant. Verify with unit tests on fixture data that `A-1042` links to `HH-RIDGELINE` and
  its firm, that a mention edge exists, that two documents sharing `NW-INST-2026-083` link to one fee schedule, that
  `NW-CANARY-7731-HH0005` is not a fee schedule, and that no property contains the canary text.
- [x] 4.2 Implement `CodeGraphBuilder` with one `CSharpCompilation` over the repository's C# (see design D4), and a
  semantic walker that emits Project, File, Type and Method nodes, `CALLS` edges for in-repo targets only, and
  `DocumentationCommentId` keys and line spans. Verify with a unit test over a small fixture solution that the call
  edges resolve across two projects, that external calls produce no node, and that an uncompilable file is counted
  but does not fail the build.
- [x] 4.3 Add the `graph [--only billing|code]` command, with the existing `IndexProgressBar` over `ConsoleProgress` (phases, and
  items out of the total), a JSON summary on stdout, and a named error with a non-zero exit when Neo4j is
  unreachable. Verify the terminal and CI output modes by hand, and the unreachable path with a unit test.
- [x] 4.4 Make: add `graph` (`## ` help comment, depends on `infra` and the indexer), add `neo4j` to `infra`, and make
  `index` end with `graph`. Extend `scripts/index_if_empty.sh` with a `cypher-shell` count check. Add a Neo4j
  reachability check to `scripts/doctor.sh`, and the 7175 URL to `banner`. Verify `make help`, `make graph` twice (the
  second run writes 0 and removes 0), and `make doctor`.

## 5. Billing graph tool

- [x] 5.1 Add the `BillingRelationships` DTO in `Maf.Lab.Domain/Graph`, with a node cap and a truncated flag, and
  without notes or fee amounts. Add `trace_billing_relationships` in `Retrieval/Tools/BillingGraphTools.cs`
  (an account id, a household id or a fee schedule code; read-only annotations; depth 1–2; `ToolErrors` for failures, an unknown id and another firm's id answered
  identically). Register it in `Program.cs`. Verify with unit tests on a substituted `TenantScopedGraph`.
- [x] 5.2 Write the "use when / do not use for" description, naming `search_documents`, `get_billing_run_status` and
  `search_billing_runs`. Verify with a tool-list test asserting the description content and that there is no
  tenant or firm field in the schema.

## 6. Code graph tools

- [x] 6.1 Add the `CodeTrace` and `ChangeImpact` DTOs. Add `trace_code_symbol` (callers or callees, depth 1–3,
  candidates listed when ambiguous, an empty result with a `search_codebase` hint) and `change_impact` (a
  repository-relative path only, rejecting `..` and absolute paths before any query, tests grouped by file) in
  `CodeSearch/Tools/CodeGraphTools.cs`. Register them and call `AddGraphStore()`. Verify with unit tests for each
  scenario in `specs/code-graph`.
- [x] 6.2 Write the descriptions naming `search_codebase` and `ask_codebase`. Verify with a tool-list test.

## 7. Integration tests (Testcontainers)

- [x] 7.1 Add a `Neo4jFixture` (a collection fixture, so only the graph tests start it; the same pinned image as compose). Verify that it starts and that
  `VerifyConnectivityAsync` passes.
- [x] 7.2 Add a leakage test: build a graph where a firm B node is bridged through a shared node to a firm A account,
  trace it as firm A, and assert that only firm A and shared nodes come back. Also assert that a firm B id answers
  like an unknown id.
- [x] 7.3 Add an idempotency test: build twice and assert 0 writes and 0 removals on the second run. Remove one seed
  account, rebuild, and assert that only its node and edges are gone.
- [x] 7.4 Add a code graph test over the real repository: the edge `DocumentSearchService → TenantScopedSearch.QueryAsync`
  exists, and `change_impact` on `TenantScopedSearch.cs` includes `TenancyAcceptanceTests`.
- [x] 7.5 Add a degradation test: with Neo4j stopped, a graph tool returns the unavailable error without hostnames,
  and `search_documents` still answers.

## 8. Topology and telemetry

- [x] 8.1 Add node `neo4j` ("Graph store"), its connectivity probe, and the edges from `mcp-retrieval` and `mcp-code`
  to `TopologyProbe`. Add the node and edges to `docs/topology.drawio` without overlaps. Verify that `TopologyTests`
  passes and that `/api/topology` lists `neo4j` healthy in the running stack.
- [x] 8.2 Verify in Jaeger that a graph tool call shows a `graph.read` span with the query name and counts, and no
  argument values. Verify that `make logs SERVICE=mcp-retrieval` shows no ids from the message.
  - Seen on 2026-10-03: `graph.read` with `graph.query=billing_neighbourhood_2`, `graph.rows=6`, `graph.truncated=false`;
    no argument value in the trace, and no id or name from the message in either mcp-retrieval replica's log.

## 9. Evals

- [x] 9.1 Add selection cases to `evals/selection.jsonl`, in English and Bulgarian, for `trace_billing_relationships` (households
  and accounts, and fee schedules such as `NW-INST-2026-083`),
  `trace_code_symbol` and `change_impact`, plus negative cases where a procedural or code question must not call them.
  Run `make eval SUITE=selection` and `make eval SUITE=domain`. Verify no regression against `evals/baseline.json`, and
  update the baseline only after the run is accepted.
  - Run on 2026-10-03. The first selection run failed s-79 to s-82: the codebase server's agent allow-list offered only
    `search_codebase`, and under system.v4 the model answered "which tests cover <file>" from snippets that merely
    mention the path. Fixed by adding the code-graph tools to the allow-list and by system.v5, which names the graph
    tools (DECISIONS §75). On v5 selection, generation and injection pass, and s-75 to s-84 all match. Selection and
    generation baselines were accepted on v5. Domain is unchanged by this branch; its crossingPrecision drop
    (0.947 → 0.905) comes from `domain.jsonl` growing to 81 cases after its baseline and is left for `main`.

## 10. End-to-end checks

- [x] 10.1 Starting from `make down` and removing the `neo4j-data` volume, run `make`. Verify that the graph is built
  by `index-if-empty`, that a chat question as firm B ("which households use fee schedule NW-INST-2026-083?") calls
  `trace_billing_relationships` and answers correctly, and that `make verify` and `make test` pass.
  - Done on 2026-10-03: `index-if-empty` built the graph on first start (billing 787 nodes / 497 edges, code 4706 /
    14324; the one billing rejection is the `data/unowned` fixture). The firm B question called
    `trace_billing_relationships` and named all five households. `make verify` first failed on its exact billing
    `tools/list`, which did not yet expect `trace_billing_relationships`; fixed in `scripts/verify_lb.sh`. `make test`
    passes (1463 .NET, 607 web).
- [x] 10.2 Run `make lint` and `make specs` (`openspec validate --all --strict`). Verify that both pass.
  - Both pass. The xUnit1051 error already on `main` (`tests/Maf.Lab.Tests/ModelAvailabilityTests.cs:233`) was fixed on
    this branch, as the user chose.

## 11. Documentation

- [x] 11.1 README.md: add `neo4j` to the services and architecture, add Neo4j Browser (7175) to the inspectors, and
  describe the graph tools.
- [x] 11.2 CLAUDE.md: add the inspector URL, the graph read-path non-negotiable ("one method runs graph reads and
  applies the tenant filter; Cypher never comes from the model"), and `make graph`.
- [x] 11.3 openspec/project.md: add Neo4j and the Bolt driver to the tech stack, `neo4j` and `neo4j-browser` to the
  compose service list, and the graph read-path rule to the conventions. `.github/copilot-instructions.md` gets the
  same rule.
- [x] 11.4 docs/telemetry.md: add the `graph.read` and `graph.write` spans and the duration metric, if span names are
  listed there.
- [x] 11.5 Run `make docs` to regenerate the make-targets, repo-layout and project-context blocks. Never edit inside a
  `generated:` block. Then run `make docs-check`, and verify it passes.
