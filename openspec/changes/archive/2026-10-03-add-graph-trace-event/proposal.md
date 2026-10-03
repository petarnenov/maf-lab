# Proposal

## Why

The monitor's timeline gives a Qdrant search an event of its own. The timeline shows it as a `retrieval` kind tag.
There is no literal "qdrant" tag: Qdrant is named only by the `qdrant <ms>` timing chip in the Retrieval view's
search card, next to the tenant scope and the settings. A graph tool call (`trace_billing_relationships`,
`trace_code_symbol`, `change_impact`) appears only as a generic `tool.call` and `tool.result`. The trace never says
that Neo4j was read, which named Cypher templates ran, how many rows they returned, whether a result was truncated,
how long Neo4j took, or that Neo4j was unavailable. `TenantScopedGraph.ReadAsync` already records all of this on its
`graph.read` span. Today it is visible only in Jaeger, and only for whoever opens the right trace there.

The fix mirrors `retrieval`. A `graph` kind gets its own timeline tag and colour. Neo4j is named where it can be
seen: in the row's title ("Neo4j billing_neighbourhood_2 · 6 rows"), and in a `neo4j <ms>` timing chip on the
detail card, styled like the `qdrant <ms>` chip.

## What Changes

- **A new trace kind, `graph`.** It is one event per graph tool call that read the graph, recorded right after that
  call's `tool.result`. The data is `{ callId, tool, instance, tenantScope, reads: [{ query, limit, rows, truncated,
  durationMs, outcome, errorType }], rows, truncated, durationMs, outcome }`. The event's duration is the time spent
  in Neo4j. The title reads `Neo4j billing_neighbourhood_2 + firm_runs · 9 rows`; the timeline adds ` · 14 ms`.
- **Structure only.** The event holds template names, counts, timings, the tenants the read path bound, and the
  outcome. It holds no argument value (entity id, symbol, path or method keys), no node property and no exception
  text. The arguments are already in the same call's `tool.call` event, joined by `callId`.
- **Same transport as `retrieval`.** The api already asks every MCP tool for diagnostics with the request `_meta`
  flag `maf-lab/trace` (`Agent:TraceRetrieval`). When that flag is set, a graph tool returns its reads in the result
  `_meta` under a new key, `maf-lab/graph`, next to `maf-lab/instance`. The api lifts the key out of the recorded
  `tool.result`, so the model never sees it, and records the `graph` event. No new channel is added, and nothing
  travels on the AG-UI stream.
- **Recorded at the one read path.** `TenantScopedGraph.ReadAsync` adds each read's template name, limit, rows,
  truncation, duration and outcome to a per-call read log, which a graph tool opens only when diagnostics are
  requested. `IGraphReader`'s signature does not change, and no tool can add or change an entry.
- **Unavailable says so.** When Neo4j cannot be reached, the read is recorded with the outcome `unavailable` and the
  exception's type name. It carries no hostname and no message. The title then reads
  `Neo4j billing_neighbourhood_2 · unavailable`. A failed read records `error`.
- **Monitor.**
  - The timeline gives `graph` its own colour token, `--kind-graph`, in light and dark.
  - The Retrieval view shows a "Graph" card per call: tool, instance, scope, total rows and a `neo4j <ms>` timing chip, and a table
    of reads.
  - The header shows "N graph reads" when the turn has any.
  - The views follow the time-travel cursor like every other view.
  - A stored trace from before this change has no `graph` event and renders as before.
- **Progress feedback:** no new CLI command, make target or UI-started process. A graph read takes milliseconds
  inside a chat turn whose progress is already shown.

Non-goals:
- A dedicated monitor tab for the graph. The graph card sits in the Retrieval view next to the Qdrant searches.
- Tracing the indexer's graph build or maintenance reads. Those are not part of a chat turn.
- Recording graph diagnostics when `Agent:TraceRetrieval` is off. Unlike a Jev relevance judgment, a graph read makes
  no paid request whose visibility would need the summary to be always sent.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `turn-tracing`: adds *Graph reads are traced*, the `graph` event, its fields and its title.
- `graph-store`: *Graph logs carry structure only* covers the turn trace's `graph` event as well as logs and spans.
- `web-ui`: adds *The monitor shows graph reads*: the timeline colour, the Retrieval view's graph card and the header
  count.

## Impact

- **Code:**
  - `src/Maf.Lab.Retrieval/Graph/GraphReadLog.cs` (new): the per-call read log and the `_meta` attachment.
  - `src/Maf.Lab.Retrieval/Graph/TenantScopedGraph.cs`: records each read.
  - `src/Maf.Lab.Retrieval/Tools/BillingGraphTools.cs` and `src/Maf.Lab.CodeSearch/Tools/CodeGraphTools.cs`: open the
    log when diagnostics are asked for, and attach it.
  - `src/Maf.Lab.Domain/Tracing/TraceEvent.cs`: `TraceKinds.Graph`.
  - `src/Maf.Lab.Api/Agent/ToolSource.cs`: `TraceMeta.Graph`.
  - `src/Maf.Lab.Api/Agent/ChatTurnRunner.cs`: lifts the key and records the event.
  - `src/Maf.Lab.Api/Agent/Tracing/GraphTraceEvent.cs` (new): the title and summary.
  - Web:
    - `web/src/monitor/traceData.ts`, `kindColors.ts`, `MonitorTabs.tsx`, `MonitorPanel.tsx`, `fixtures.ts`;
    - `web/src/index.css` (`--kind-graph`).
- **API:** `GET /api/turns/{turnId}/trace` and the live trace API may return events of the new kind. This is additive.
  Clients already render unknown kinds.
- **MCP:** graph tool results carry `_meta["maf-lab/graph"]` and `_meta["maf-lab/instance"]` only when the caller
  asked with `maf-lab/trace`. The structured content and the output schema do not change.
- **Data:** none. Stored traces are unchanged.
- **Dependencies:** none. No package version moves.
- **Tests:**
  - .NET unit tests for the read log, `TenantScopedGraph`'s recording (success and unavailable), the three tools'
    `_meta`, the api's lifting and title.
  - Vitest for the colour, the graph card, the header chip, as-of-step behaviour and an old trace without the kind.

## Documentation impact

- **docs/trace-events.md:** a `graph` row in the kinds table and its place in the typical order, plus the `_meta`
  key in the `tool.result` row's list of lifted keys.
- **docs/telemetry.md:** the `graph.read` span row says the same numbers also reach the turn trace's `graph` event.
- **README.md:** the code-graph paragraph says each graph read shows in the monitor's timeline as a `graph` event.
  This is not a generated block.
- **openspec/project.md, CLAUDE.md, .github/copilot-instructions.md:** no rule, command, route, project or model
  changes, so none is affected.
