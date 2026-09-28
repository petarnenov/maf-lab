# Design

## Context

- **Where the relevance judgment lives today:** only inside the retrieval diagnostics.
  - `DocumentSearchService.RankAsync` asks `JevRelevanceJudge` once per search.
  - It writes the judgment into `SearchDiagnostics.Relevance`, and only when a diagnostics object was passed in.
  - `SearchDocumentsTool` creates that object only when the request's `_meta` carries `maf-lab/trace`.
  - The api sets that flag only when `Agent:TraceRetrieval` is on.
  - `ChatTurnRunner.TraceToolResult` then turns the diagnostics into one `retrieval` event with no duration.
- **What the Jev statistics read:** `JevStatistics` counts judged searches from `retrieval.data.relevance`. It counts
  tool-result screening requests as the number of `items` in the `guardrail` event.
- **How the guardrail event gets its duration:** `Guardrail.Trace` sets it to the slowest item. Items run in parallel
  under `Guard:MaxConcurrent` (8), so when there are more than 8 items the bar understates the screening.
- **The A2A path:** `AssistantBridge` calls `Guardrail.Trace(null, …)`. There is no turn trace, only a log line. This
  stays, per the decision to fix the wording rather than count A2A calls.

## Goals / Non-Goals

**Goals:**
- The relevance judgment gets its own timeline row, independent of the diagnostics switch.
- The statistics read the explicit records, and old stored traces still count correctly.

**Non-Goals:**
- No change to the shape of Jev's requests, to the gate's logic, or to the A2A path's tracing.
- No migration of stored traces.
- No new admin endpoint or response field.

## Decisions

**1. The judgment gets a new trace kind, `relevance`, rather than a richer `retrieval` title.**
- A separate event gives the judge its own bar with a real duration, and it exists when `retrieval` does not
  (diagnostics off).
- It also gives the statistics one uniform source.
- *Alternative:* retitle the `retrieval` event and give it `relevanceMs` as its duration. That still vanishes with the
  diagnostics, and it presents the whole search as if it took as long as the judge. Rejected.
- The event is added right after the `tool.result` event, and after `retrieval` when that exists.
- Its data is the summary (decision 2) plus `callId`.

**2. The server always attaches a numbers-only summary under a new `_meta` key, `maf-lab/relevance`.**
- `SearchOutcome` gains `Relevance` (the summary or null), built in `RankAsync` whenever a judgment was made.
- `SearchDocumentsTool` puts it in `_meta` whether or not tracing was requested.
- Summary fields: `gate`, `reranker`, `floor`, `judged`, `max`, `silenced`, `rerankedByJev`, `model`, `durationMs`,
  `reason`. No chunk ids or scores per chunk. Those stay in the full diagnostics.
- `reranker` is the configured reranker, which keeps the statistics' "judged with the Jev reranker" meaning unchanged.
  `rerankedByJev` means Jev's answer actually ordered the results: false when the search was silenced or Jev did not
  answer. It drives the monitor's label.
- *Alternative:* have the api send a second opt-in flag. That adds a knob with nobody who would turn it off, since the
  summary carries no content. Rejected.
- *Alternative:* recompute the summary in the api from the diagnostics. That does not work when the diagnostics are
  off. Rejected.
- In the api, `TraceMeta` gains `Relevance = "maf-lab/relevance"`.
- `TraceToolResult` strips the key from the recorded raw result, as it does for the diagnostics, and emits the
  `relevance` event from it.
- The model never sees `_meta`, because `ToolDataEnvelope.Unpack` reads the structured content only. A test pins this.

**3. The statistics count relevance per turn: `relevance` events if the turn has any, otherwise `retrieval.relevance`.**
- A turn recorded after this change always has `relevance` events for its judged searches.
- A turn recorded before it has none, so choosing the source per turn cannot count one search twice.
- Both paths count "judged with the Jev reranker" as `reranker == "jev"`. For a `relevance` event it is read from the
  event, and on the fallback path from `settings.reranker`.

**4. Tool-result screenings record `requests` and a wall-clock duration.**
- `ScreenToolResultAsync` times `ScreenAllAsync` with a stopwatch. `requests` is the count of items whose text was
  non-empty, since an empty item makes no Jev call.
- `ScreenedToolResult` carries both values. `Guardrail.Trace` takes an optional `requests` and `elapsedMs`.
  - When they are given, the event's duration is `elapsedMs`, and the title appends "· N Jev requests" when N > 1.
  - The reviewer check passes `requests: 1`.
  - The prompt check passes nothing. It rides in the intent request, and the statistics already exclude it.
- The statistics use `requests` when present, and otherwise the item count, which was the old behaviour.
- The latency percentiles per site keep using each item's `durationMs`. That is the latency of each Jev request, which
  is what the site breakdown means.

**5. The web renders the new kind without a special timeline case.**
- `TimelineTab` already renders any kind. `kindColor` gives `relevance` the colour of the retrieval family.
- `RetrievalTab` gets a "Jev relevance" block for each search:
  - When `retrieval.data.relevance` exists, it is used, including the probability of each chunk.
  - Otherwise the block uses the `relevance` event with the same `callId`.
  - A turn with `relevance` events but no `retrieval` events shows those blocks instead of "No retrieval in this turn".
- The Jev page adds one line of scope text under its header, and the guardrail panel's labels drop "partner".

## Risks / Trade-offs

- [Other MCP clients now receive `maf-lab/relevance` in `_meta` they never asked for.] → It holds numbers only and no
  content, and the spec forbids ids and text. `_meta` is ignorable by protocol.
- [A trace from a replica that is mid-deploy may mix an old mcp-retrieval (no summary) with a new api.] → The api
  falls back to `retrieval.relevance` when there is no summary. Decision 3's choice per turn also covers the
  statistics.
- [The tool-result bar gets longer, because it is now wall-clock.] → That is the point. The latency of each item stays
  in the event for the percentiles.
- [Traces grow by one small event per judged search.] → It is well under the trace size limits.

## Migration Plan

- Deploy mcp-retrieval and api together with `make`. Either order is safe because of the fallbacks above.
- Rollback is a plain revert. Stored traces with `relevance` events stay readable by the old code, which ignores
  unknown kinds.
