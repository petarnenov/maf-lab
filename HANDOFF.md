# Handoff: add-neo4j-graph (branch `feat/neo4j-graph`)

Read this first. Delete this file, and the pointer to it at the top of `CLAUDE.md`, in the commit that finishes the
last open item below, before the branch is merged.

## Where things stand

The OpenSpec change `add-neo4j-graph` is implemented and **archived** in
`openspec/changes/archive/2026-10-02-add-neo4j-graph/`. Its specs are synced into `openspec/specs/`:
`graph-store`, `billing-graph` and `code-graph` are new, and `tenant-isolation` and `system-topology` are modified.
Nothing is merged into `main`, and no PR exists.

What was built:
- **Neo4j in compose.** It runs `neo4j:2026.09.0-community`, with Bolt on `127.0.0.1:7687` and the dev-only
  `neo4j-browser` inspector on `127.0.0.1:7175`.
- **The read path.** One tenant-scoped read path, `TenantScopedGraph.ReadAsync`, runs a fixed set of Cypher templates
  from `GraphTemplates`. One write path, `TenantScopedGraphMaintenance`.
- **The build.** `make graph` builds the billing graph and the code graph (Roslyn), and `make index` ends with it.
- **The tools.** `trace_billing_relationships` on `mcp-retrieval`, and `trace_code_symbol` and `change_impact` on
  `mcp-code`.
- **Records.** `DECISIONS.md` §75 holds the pins and the reasons. The design is in the archived `design.md`.

Verified in the cloud session:
- 1401 unit, 62 integration and 607 web tests pass.
- `make docs-check` passes, and `openspec validate --all --strict` passes.
- Two consecutive real graph builds over the repository; the second wrote 0 and removed 0.

## What is next, in order, and why

These are the four unchecked tasks in the archived `tasks.md`. Each one needed something the cloud session did not
have.

1. **Run the evals.** `make eval SUITE=selection`, then `make eval SUITE=domain`.
   - *Why:* the tool set changed, and the project rule says evals run when the tool set changes. Selection cases
     s-75 to s-84 (English and Bulgarian) were added to `evals/selection.jsonl` but never run, because the session had
     no `OLLAMA_API_KEY` or `JEV_MAF_LAB`.
   - *How:*
     - Compare the results against `evals/baseline.json`.
     - Update the baseline (`make eval-accept`) only once the run is accepted.
     - If the graph cases fail, fix the tool descriptions in `BillingGraphTools.Description` or
       `CodeGraphTools.TraceDescription` / `ImpactDescription` first. Do not loosen the cases.
     - Codebase cases expect `search_codebase` as well as the graph tool, because a codebase turn always searches
       first (intent-classification).
2. **End to end on a fresh stack.**
   - *How:* run `make down`, then `docker volume rm maf-lab_neo4j-data`, then `make`.
   - *Why:* this proves `index-if-empty` builds the graph on first start, which was only verified piecewise.
   - *Then:*
     - As a firm-b user, ask in the chat "which households use fee schedule NW-INST-2026-083?". Expect
       `trace_billing_relationships` and a correct answer.
     - Run `make verify` and `make test`.
     - Open http://localhost:7175 and connect to `bolt://localhost:7687` as `neo4j` / `maf-lab-dev-graph`.
3. **Check the spans in Jaeger.** After a graph tool call, Jaeger (`/jaeger`) should show a `graph.read` span with
   `graph.query`, `graph.rows` and `graph.truncated`, and no argument values.
   - *Why:* the graph-store spec says graph logs carry structure only.
   - Also check that `make logs SERVICE=mcp-retrieval` shows no ids from the message.
4. **`make lint`.**
   - *Why:* it stops on one error that is already on `main`, not from this change:
     `tests/Maf.Lab.Tests/ModelAvailabilityTests.cs:233`, xUnit1051 (pass `TestContext.Current.CancellationToken`).
   - *What to do:* ask the user whether to fix it here or on `main`. Nothing this change adds warns.

When all four are done:
- Tick them in `openspec/changes/archive/2026-10-02-add-neo4j-graph/tasks.md` and remove their "*Open:*" notes.
- Delete this file and the `CLAUDE.md` pointer.
- Run `make docs-check`.
- Ask the user before opening a PR to `main`.

## Things that will bite

- **Docker Hub rate limits (429)** on `neo4j` / `alpine/socat` / `qdrant` pulls. The cloud session pulled through
  `mirror.gcr.io/library/...` and re-tagged.
- **The Neo4j .NET driver 6.x always opens an IPv6 dual-mode socket** (DECISIONS §75). That is fine on a normal host.
  The cloud sandbox's kernel had no IPv6 at all, so tests there ran under a test-only `LD_PRELOAD` shim that is not in
  the repo. Locally you should need nothing.
- **`NEO4J_PASSWORD` is set when the volume is first created.** After that, changing it means removing
  `maf-lab_neo4j-data`.
- **The billing corpus never names the seed account ids** (A-1042, HH-RIDGELINE…), so account and household mention
  edges are empty on real data. Fee schedules (firm-b, about 120 codes) are what make the billing graph useful. This
  was decided with the user.
- **Not done on purpose, as follow-ups:**
  - Jev routing for the graph tools (design Non-Goals).
  - A graph-vs-Qdrant section in `make drift`.
