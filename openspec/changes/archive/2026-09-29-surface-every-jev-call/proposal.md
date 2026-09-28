# Proposal

## Why

A map of every place the code calls Jev (2026-09-28) found five production call sites. The monitor's timeline shows
three of them in full. It shows the fourth, the passage-relevance judge, only as a field inside the expanded JSON of
the `retrieval` row. That row's title does not name Jev, the gate's verdict or the judge's latency. When
`Agent:TraceRetrieval` is off, the judgment disappears from both the timeline and the Jev statistics. The fifth site,
the A2A partner path, is never counted, yet the statistics page claims to cover "every Jev call site". A lab that
exists to observe its own internals should not hide one of its Jev requests or overstate what its statistics cover.

## What Changes

- Every search that asks Jev for a relevance judgment adds its own `relevance` event to the turn trace:
  - The title names Jev, the highest probability against the floor, and whether the gate silenced the search, left it
    ungated because Jev was unavailable, or kept it.
  - The event carries the judge's latency as its duration, so it shows as its own bar on the timeline.
  - The event is recorded whether or not retrieval diagnostics are requested.
- `search_documents` always returns a small relevance summary in its result `_meta` when it asked Jev for a
  judgment:
  - The summary holds numbers only: gate, floor, judged count, max, silenced, model, duration, reason, and whether Jev
    reranked.
  - It has no chunk ids, no scores per chunk, and no text.
  - With the full diagnostics requested, nothing changes.
- The monitor's retrieval view gains a relevance section:
  - Jev's model, the floor, the highest probability, and the gate's verdict, or the reason Jev did not answer.
  - Each judged candidate's probability, when the diagnostics carry it.
- A tool-result screening records how many Jev requests it made and how long the whole screening took:
  - The event's duration is the screening's wall-clock time, not the slowest item.
  - The title says how many requests there were when there was more than one.
- The Jev statistics prefer these explicit records when counting:
  - Requests come from a screening's own request count.
  - Judged searches come from `relevance` events.
  - Traces stored before this change are read as before, and nothing is counted twice.
- The Jev statistics endpoint, screen and spec say what they cover: Jev calls made by chat turns. They note that the
  screenings of the A2A partner path are logged but not counted. The guardrail section drops the "partner" screenings
  it could never have counted.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `turn-tracing`:
  - Every Jev relevance judgment is recorded as its own `relevance` event.
  - A tool-result `guardrail` event records its request count and its wall-clock duration.
- `retrieval-tool`: a search that asked Jev returns a numbers-only relevance summary in `_meta` even without the
  trace flag.
- `jev-statistics`:
  - The scope is stated as chat turns, with the A2A path named as not counted.
  - The partner screenings are removed from the guardrail section.
  - The counts come from the new explicit records, falling back to older traces without double counting.
- `web-ui`:
  - The retrieval view shows the relevance judgment.
  - The Jev screen states its scope.

## Impact

- **mcp-retrieval:** `SearchDocumentsTool` and `SearchDiagnostics` (relevance summary in `_meta`).
- **api:**
  - `ChatTurnRunner.TraceToolResult` (new `relevance` event; the summary `_meta` key is stripped from the recorded
    result).
  - `TraceMeta`, `TraceKinds.Relevance`.
  - `Guardrail` (request count, wall-clock timing and title for tool-result screenings).
  - `JevStatistics` (reads `relevance` events and `requests`, with a fallback for older traces).
- **web:**
  - `MonitorTabs.RetrievalTab` (relevance section).
  - `traceData.ts` (types).
  - `kindColors.ts` (a colour for `relevance`).
  - `JevPage.tsx` (scope note).
- **docs:** `docs/trace-events.md` (`relevance` kind, `requests` on `guardrail`).
- **Not changed:**
  - No new endpoint.
  - No stored-trace migration: old traces stay readable.
  - The A2A path's tracing and Jev's request shapes.
