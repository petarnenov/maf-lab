# Design

## Context

See proposal.md. Relevant current code:
- `ChatTurnRunner` runs a turn. Its function middleware already sees each tool call before and after execution, and it
  writes SSE events into a channel.
- `RequiredToolModeChatClient` wraps the chat client.
- `SqliteChatHistoryProvider` builds the history window.
- `McpToolSource` lists tools over MCP per turn.
- `DocumentSearchService.RankAsync` / `TenantScopedSearch` do hybrid search in the MCP server.
- In the web app, `ChatPage` renders messages, tool cards and sources from `chatReducer`, fed by `useChatStream`.

## Goals / Non-Goals

**Goals:**
- One trace model, captured in-process, streamed live and persisted, covering every step listed in the turn-tracing spec.
- Retrieval internals come from the process that has them (the MCP server), without changing what the model sees.
- Tenant-safe and log-safe by construction.

**Non-Goals:**
- An OpenTelemetry exporter, collector or external APM. The trace model stays compatible with GenAI span naming, so
  exporting could be added later.
- Cross-turn analytics dashboards.
- Tracing the indexing pipeline and admin jobs.

## Decisions

### D1. Trace model and collector
`Maf.Lab.Domain.Tracing.TraceEvent(int Seq, long AtMs, string Kind, string Title, long? DurationMs, JsonElement Data,
bool Truncated)`. The kinds are `turn.start`, `intent`, `history`, `prompt`, `model.request`, `model.response`,
`tool.forced`, `tool.call`, `tool.result`, `retrieval`, `envelope`, `tool.unknown`, `audit`, `sources`, `signals`,
`memory`, and `turn.end`.

A scoped `TurnTrace` collector is created per turn inside `ChatTurnRunner`:
- It assigns `Seq` and `AtMs` from a `Stopwatch`.
- It applies the size caps (20,000 characters per string, 1 MB total; past the total cap, later events keep their kind
  and timing with data replaced by `{"truncated":true}`).
- It writes each event to the SSE channel as `trace` and appends it to an in-memory list that is persisted at turn end.

*Alternative:* OpenTelemetry `ActivityListener` over the M.E.AI and Agent Framework activity sources (with
EnableSensitiveData). It was rejected as the only source: span attributes are flattened strings, the MCP and retrieval
internals are not in them, and ordering and streaming would still need our own collector.

### D2. Capture points
- **Runner:** `turn.start`, `intent`, `prompt` (system prompt text and the offered tools' `JsonSchema`), `sources`,
  `signals`, `turn.end`.
- **History:** `SqliteChatHistoryProvider` reports the window it built (the messages it included and left out, and the
  token budget) through a callback into the collector (`history`). It also reports rows stored (`memory`).
- **`TracingChatClient : DelegatingChatClient`,** placed between `RequiredToolModeChatClient` and the provider client,
  so it records only real model calls:
  - `model.request`: the messages serialised with M.E.AI JSON options, plus tool mode, temperature, the think flag and
    the tool names.
  - `model.response`: the aggregated streaming updates, i.e. text, `FunctionCallContent`s, finish reason,
    `UsageContent` or `ChatResponse.Usage`, latency, model id, and the endpoint host from `ChatClientMetadata`.
  - `RequiredToolModeChatClient` emits `tool.forced` when it issues a call itself.
- **Tool middleware:**
  - `tool.call` before execution, with full arguments. The existing SSE `tool_call_started` stays.
  - After execution: `tool.result` with the raw `CallToolResult` JSON (structured content, text content, isError and
    `_meta` minus diagnostics) and latency; `retrieval` with `_meta["maf-lab/trace"]` when present; `envelope` with the
    exact string returned to the model; and `audit` with the row written.
  - Unknown tools: `tool.unknown`, recorded where they are detected today.

### D3. Retrieval diagnostics through MCP `_meta`
- The agent sets `_meta: {"maf-lab/trace": true}` on tool calls (`McpClientTool.WithMeta`).
- In `SearchDocumentsTool`, `RequestContext.Params.Meta` is checked. When the flag is set, `DocumentSearchService`
  runs with a `SearchDiagnostics` collector:
  - it records settings, the BM25 query terms with IDF, the dense model and dims, and the embed and Qdrant timings;
  - for the per-branch lists it runs the dense-only and sparse-only queries through the same
    `TenantScopedSearch.QueryAsync(principal, …)` (so they carry the tenant filter) in addition to the fused query;
  - it records the rerank order.
- The diagnostics go to `CallToolResult.Meta["maf-lab/trace"]`, together with `instance` (the replica).
- `ToolDataEnvelope.Unpack` already uses structured content only, so `_meta` never reaches the model; a test asserts
  this.
- **Cost:** two extra tenant-scoped Qdrant queries per traced search. That is acceptable for the lab and visible in
  the timings. `Retrieval:TraceBranches=false` can disable it.

### D4. Persistence, retention, access
- **Table:** `TurnTraces(TurnId PK, FirmId, UserId, CreatedAt, Json)`, created through the idempotent initializer.
- **Write:** the whole trace is written once at turn end, in the same place the turn row is written.
- **Retention:** a `TraceRetentionService` (`BackgroundService`) deletes rows older than `Tracing:RetentionDays`
  (default 7) every hour. It is replica-safe because it runs idempotent deletes.
- **`GET /api/turns/{turnId}/trace`:** returns 200 when the trace belongs to the caller (user and firm match), or when
  the caller is a FIRM_ADMIN of the same firm and the turn has signals (it is in the review queue). Otherwise 404.
- **Logs:** trace data is never logged; the existing logging test is extended to a traced turn.

### D5. Web
- **Layout:** `ChatPage` becomes a CSS grid with `minmax(420px, 1fr)` for the chat and `minmax(480px, 1.2fr)` for the
  monitor, stacking under 1024 px. The chat keeps its current components.
- **State:** a pure `traceReducer` keyed by turn id accumulates live `trace` events; `chatReducer` routes the `trace`
  event there. A selected turn is loaded with TanStack Query from `/api/turns/{id}/trace`.
- **`MonitorPanel` tabs:**
  - **Timeline:** a waterfall with elapsed and duration bars, colour by kind, click to expand the raw JSON.
  - **Model:** one card per iteration, with request messages (role-coloured, tool results collapsed), tools and tool
    mode, response, tokens and latency.
  - **Retrieval:** per search call, scope and settings chips, a query-term table with IDF, three side-by-side ranked
    lists (dense, sparse, fused) with scores, and the rerank order.
  - **MCP:** raw arguments and results, with replica badges for the api and mcp replicas.
  - **Prompt & memory:** the system prompt, tool schemas and the history window with token bars.
- **Rendering:** a small, dependency-free `JsonView` component renders the raw data.
- **Review queue:** the feedback review form gets an "Open trace" toggle that renders the same `MonitorPanel`.

## Risks / Trade-offs

- **[Trace size and SSE volume]:** full messages on every iteration can be large. Mitigated by the caps, and by tool
  results appearing once per model call with long snippets truncated. The monitor renders lazily per tab.
- **[Sensitive content in the database]:** traces hold message content. They live in the content store with a 7-day
  retention, are scoped to the owner and the firm admin, and are never logged.
- **[Extra Qdrant load from branch lists]:** two extra scoped queries per traced search. There is a config switch, and
  the extra work shows up in the retrieval timings.
- **[Provider differences in usage reporting]:** token usage is shown when the provider reports it (Ollama reports
  prompt and eval counts; the stub reports placeholders). The UI shows "n/a" otherwise.
