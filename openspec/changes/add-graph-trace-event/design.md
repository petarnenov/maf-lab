# Design

## Context

- **How `retrieval` gets into the turn trace.**
  - With `Agent:TraceRetrieval` on (the default), `ToolSource` marks every MCP tool it offers with the request
    `_meta` flag `maf-lab/trace: true`.
  - `search_documents` and `search_portfolio_documents` see the flag (`SearchDocumentsTool.TraceRequested`). They
    return their diagnostics in the result's `_meta["maf-lab/trace"]`, with `_meta["maf-lab/instance"]` beside it.
  - `ChatTurnRunner.TraceToolResult` lifts those keys out of the recorded `tool.result` and adds a `retrieval` event
    after it.
  - `_meta["maf-lab/relevance"]` follows the same path into the `relevance` event.
  - None of this reaches the model or the AG-UI stream.
- **What the graph read path knows.** `TenantScopedGraph.ReadAsync` is the one read path (graph-store). For every
  read it already records the following on the `graph.read` span and the `maf.graph.query.duration` histogram:
  - the template name (`graph.query`);
  - the rows (`graph.rows`) and truncation (`graph.truncated`);
  - the duration (`graph.duration_ms`) and the outcome.

  The graph tools never see those numbers: they get the mapped result.
- **One tool call can make several reads.**
  - `trace_billing_relationships` reads `billing_neighbourhood_{1,2}` and, for an account, `firm_runs`.
  - `trace_code_symbol` reads `symbol_candidates`, then `callers_N` or `callees_N`.
  - `change_impact` reads `file_methods`, then `callers_4`.
- **Constraints.**
  - `IGraphReader.ReadAsync(principal, query, ct)` is the contract that every tool and test fake implements.
  - Graph logs carry structure only.
  - No new transport between the agents and the web (agui-protocol-only).
  - The trace is a message-content store, but the graph-store rule asks for structure.

## Goals / Non-Goals

**Goals:**
- A graph tool call is visible in the timeline as a Neo4j step, the way a search is visible as a Qdrant step.
- The event's numbers are the read path's own, the same numbers as the span's.
- Graph unavailable is legible in the trace without leaking a hostname or an exception message.

**Non-Goals:**
- Tracing graph builds or maintenance reads.
- Changing what a graph tool returns to the model.
- A new monitor tab.

## Decisions

1. **One `graph` event per tool call, not per read.**
   - The timeline row answers "what did this call cost in Neo4j": the tool's reads in order, then the totals.
   - One event per read would put two or three short rows under every call, and they would need re-grouping by
     `callId` in every view.
   - The kind is named for what was read, `graph`, the way `retrieval` names a search rather than Qdrant. The title
     names Neo4j, so the tag reads "graph" and the row reads "Neo4j …".
2. **A separate `_meta` key, `maf-lab/graph`, behind the existing `maf-lab/trace` request flag.**
   - The api treats any object under `maf-lab/trace` as Qdrant diagnostics, so reusing that key would need a
     discriminator, and an older api would draw a broken retrieval card.
   - A key of its own follows the pattern `maf-lab/relevance` set, and costs one line in `TraceMeta`.
   - The request flag is the one the api already sends, so `Agent:TraceRetrieval` turns both kinds of diagnostics on
     and off together.
   - It is not sent unconditionally like `maf-lab/relevance`. That summary exists so that a paid Jev request is never
     invisible, and a graph read is not a paid request.
3. **The read path records; the tool only opens and attaches a per-call read log.**
   - `GraphReadLog.Begin()` opens a log in an `AsyncLocal` scope, and disposing it restores the previous scope.
   - `TenantScopedGraph.ReadAsync` appends one `GraphReadRecord` to the current log, if any, in its `finally` block.
     That is where the span's numbers already are. It also records the tenants it bound.
   - The tool attaches the log to its result with `GraphReadLog.Attach(result, log)`, success and error alike.
   - *Rejected:* a diagnostics parameter on `IGraphReader.ReadAsync`. That changes the one read path's contract,
     every caller and every fake, for an observer that must not influence the read.
   - *Rejected:* a decorating reader in the tool. It cannot see rows or truncation, because `TResult` is the mapped
     result.
   - *Rejected:* an `ActivityListener` on `graph.read`. Spans may not be listened to or sampled, and the trace must not
     depend on telemetry configuration.
   - The log has no public way to add a record, so a tool cannot write or alter one. `Add` is internal to the read
     path's assembly.
4. **No argument values in the event.**
   - The entity id, symbol name and file path are already in the same call's `tool.call` event, the full arguments,
     joined by `callId`.
   - The `CallTrace` arguments are method keys read from the graph itself, up to 300 of them.
   - Repeating either in the graph event adds no information. It would also break graph-store's "structure only",
     which the event is meant to follow. The template name already carries the depth (`billing_neighbourhood_2`,
     `callers_4`), and `limit` is recorded per read.
5. **The outcome has four values.**
   - `ok`.
   - `unavailable`: `ServiceUnavailableException`, `SessionExpiredException`, `TransientException` or
     `SecurityException`. This is the same set that `ToolErrors.ForException` maps to "temporarily unavailable".
   - `cancelled`: `OperationCanceledException`.
   - `error`: anything else.

   A record that is not `ok` carries `errorType`, the exception's type name, which is what the services already log.
   It never carries the message, because Bolt messages can name the host. The metric's own `outcome` tag stays
   `ok`/`error`, and dashboards do not change.
6. **The api builds the summary and the title.**
   - `GraphTraceEvent.From(callId, tool, diagnostics)` sums the rows and durations, ORs the truncation, and takes the
     first outcome that is not `ok`.
   - Title: `Neo4j <q1> + <q2> · <n> row(s)[ · truncated]`, or `Neo4j <q…> · <outcome>` for a call whose outcome is
     not `ok`.
   - The event's `durationMs` is the rounded total. The timeline appends it to the title, so the title itself does not
     repeat the milliseconds.
   - A malformed or empty payload yields no event, rather than a broken one.
7. **Placement:** directly after the call's `tool.result`, the same slot `retrieval` takes for a search. Graph tools
   have no `retrieval` or `relevance` event, so there is no ordering conflict.
8. **Web.**
   - `kindColor('graph')` returns `--kind-graph`, a pink, `light-dark(#be185d, #f472b6)`. That gives 6.0:1 with
     `--on-accent` in light, 7.1:1 in dark, and 6.5:1 on the dark surface. The hue is not used by any other kind.
     An orange was considered and dropped: in the light theme it sat too close to `--kind-danger`'s red.
   - The card's time chip reads `neo4j <ms>`, in the same style as a search card's `qdrant <ms>`, so the store is
     named where Qdrant is named.
   - The Retrieval view gets a graph card per event. Searches and graph reads both answer "what did the tools read
     from the stores", and a separate tab for one or two cards per turn would be noise.
   - The header chip appears only when the count is above 0, so turns without graph reads look unchanged.
   - `mcpInstances` includes the graph event's instance, like `retrieval`.

## Risks / Trade-offs

- **`AsyncLocal` is implicit.**
  - *Mitigations:* the scope is opened and disposed in the same method; `Add` takes a lock (reads are sequential
    today, but nothing should break if they run concurrently); and a test proves a read outside any scope records
    nothing and a disposed scope stops recording.
- **Diagnostics off means no `graph` event.**
  - This matches `retrieval`, and the default is on.
- **The `TraceKind` union in `web/src/api/types.ts` is not updated.**
  - It is unused (the event's `kind` is typed `string`), and it already lacks several kinds (`relevance`,
    `answer.check`, `reasoning.delta`).
  - The file is being edited by the parallel `add-graph-drift` change, so this change leaves it alone. Bringing it in
    line, or removing it, is a separate cleanup.

## Migration Plan

No data migration is needed. Stored traces are unchanged, and the web renders traces with or without the kind. An MCP
server from before this change sends no `maf-lab/graph` key, and its graph calls record no `graph` event, exactly as
today.
