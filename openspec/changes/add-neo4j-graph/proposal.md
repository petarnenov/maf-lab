# Proposal

## Why

Qdrant answers "which passages are about X". It cannot answer questions about how things connect. Billing questions
like "which accounts share Ridgeline's household, and which fee schedule applies to them?" depend on relationships.
So do code questions like "who calls `TenantScopedSearch.QueryAsync`?" and "which tests reach the code in this file?".
Today the agent can only guess these from text chunks. Neo4j adds a graph store next to the vector store, which lets
the lab exercise GraphRAG end to end. It must meet the same bars as everything else in the lab: tenant isolation,
DTO-only tool results, progress feedback and documentation sync.

## What Changes

- **New service `neo4j`** in Docker Compose:
  - Pinned image, a named volume and a healthcheck.
  - Bolt is reachable only inside the compose network.
  - Neo4j Browser is published on loopback as a dev inspector on `http://localhost:7175`, profile `inspectors`.
- **One graph database with two subgraphs:**
  - The **billing graph** is built from the seed data (`compose/seed/*.json`) and the billing corpus (`data/`). It
    holds firms, households, accounts, billing runs, fee schedules (codes found in the documents) and documents,
    and every node carries `tenant_id`.
  - The **code graph** is built from the repository with Roslyn. It holds projects, files, types and methods, linked
    by contains, declares, calls and project-reference edges. It is a shared (`tenant_id = shared`) corpus, like
    `mcp-code`.
- **One tenant-scoped read path for the graph.** A single method takes a `Principal` and runs one of a closed set of
  parameterised Cypher templates, binding the readable tenants itself. Writes go through one maintenance type keyed
  by `TenantId`. The model never writes Cypher, and no tool has a tenant argument.
- **New MCP tools:**
  - `trace_billing_relationships` on `mcp-retrieval`. It returns a bounded neighbourhood of an account, a household
    or a fee schedule as a DTO.
  - `trace_code_symbol` (callers or callees of a symbol) and `change_impact` (what a file's code is reached from,
    including tests) on `mcp-code`.
  - All three are read-only, and their descriptions say when to use the search tools instead.
- **Indexer command `graph`** in `Maf.Lab.Indexing`. It builds both subgraphs idempotently, matching nodes by stable
  keys and removing stale nodes per source.
  - It shows a progress bar through the existing `ConsoleProgress`: a live bar in a terminal, and throttled plain
    lines in CI.
  - New make target `make graph`. `make index` and `index-if-empty` also build the graph when it is empty.
- **The topology report** gains a graph-store entry and the edges `mcp-retrieval → neo4j` and `mcp-code → neo4j`.
- **Selection evals** gain cases for the three new tools. The tool set changes, so evals run per project convention.
- **No Jev call is added or changed.** Intent routing is not extended to the new tools. That is a deliberate
  non-goal, explained in design.md.
- No UI action is added, so no new UI progress is needed. Graph queries are bounded and fast, and the chat turn
  already shows its own progress.

## Capabilities

### New Capabilities
- `graph-store`: the Neo4j service, the single tenant-scoped graph read path, the graph maintenance path, and the
  `graph` indexer command with its progress bar and idempotency.
- `billing-graph`: the billing subgraph's model and sources, and the `trace_billing_relationships` tool.
- `code-graph`: the code subgraph built with Roslyn, and the `trace_code_symbol` and `change_impact` tools.

### Modified Capabilities
- `tenant-isolation`: the mandatory tenant restriction and the query-path enumeration test extend from the vector
  store to the graph store.
- `system-topology`: the topology report lists the graph store and its edges.

## Impact

- **Compose:**
  - New `neo4j` service and `neo4j-data` volume.
  - `mcp-retrieval` and `mcp-code` depend on it being healthy.
  - New `Neo4j__*` settings in `x-app-env`.
  - `NEO4J_PASSWORD` comes from the environment, with a dev default.
- **New packages** (to be pinned in DECISIONS.md §75): the `neo4j` image, `Neo4j.Driver`, and `Testcontainers.Neo4j`.
  Roslyn (`Microsoft.CodeAnalysis.CSharp`) is already pinned. `Microsoft.CodeAnalysis.Workspaces` is not added.
- **Code:**
  - `src/Maf.Lab.Retrieval` gets a new `Graph/` folder (scoped read, maintenance, templates) and a new tool.
  - `src/Maf.Lab.CodeSearch` gets two tools.
  - `src/Maf.Lab.Indexing` gets a `graph` command, the graph builders, and a Roslyn walker.
  - `src/Maf.Lab.Domain` gets the graph DTOs.
  - `src/Maf.Lab.Api/Topology/TopologyProbe.cs` gets the new node and its edges.
  - `docs/topology.drawio` gets the new node.
- **Tests:**
  - Unit tests for the templates, the template tenant check, and the extended Cecil query-path enumeration.
  - Integration tests on a Neo4j Testcontainer: cross-tenant leakage and idempotent rebuild.
  - Topology test update.
- **Make:** `graph`, plus `index`, `index-if-empty`, `doctor` (a reachability check) and `banner` (the 7175 URL).
- **Evals:** `evals/selection.jsonl` and the baseline after an accepted run.

## Documentation impact

- **README.md:**
  - The services list and architecture section gain `neo4j`.
  - The inspector URLs gain Neo4j Browser on 7175.
  - The make-targets block regenerates with `make graph`.
- **CLAUDE.md:**
  - The inspector line gains `Neo4j Browser http://localhost:7175`.
  - The non-negotiable "One method builds Qdrant queries" gains its graph counterpart: one method runs graph reads
    and applies the tenant filter, and no Cypher comes from the model.
  - `make graph` is added to the commands list.
- **openspec/project.md:**
  - The tech stack gains Neo4j and the Bolt driver.
  - The compose service list gains `neo4j`.
  - The conventions gain the graph read-path rule.
  - The `project-context` block in `openspec/config.yaml` then regenerates through `make docs`.
- **.github/copilot-instructions.md:** gains the same graph read-path rule. Its generated blocks regenerate.
- **docs/telemetry.md:** gains the graph query span and metric names, if the telemetry spec lists span names there.
  This is to be confirmed while implementing.
- **DECISIONS.md:** new §75 with the pins and the decisions recorded in design.md.
- **docs/topology.drawio:** gains the neo4j node and its edges.
- No `generated:` block is edited by hand. They are regenerated with `make docs` and checked with `make docs-check`.
