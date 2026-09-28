# Tasks

## 1. mcp-retrieval: relevance summary on every judged search

- [x] 1.1 Add a numbers-only `RelevanceSummary` to `SearchOutcome`:
  - Fields: `gate`, `floor`, `judged`, `max`, `silenced`, `rerankedByJev`, `model`, `durationMs`, `reason`.
  - Build it in `DocumentSearchService.RankAsync` whenever a judgment was made, and leave it null otherwise.
  - Verify with a test in `RelevanceJudgeTests`/`RelevanceFloorTests` that asserts the summary is set with the gate on
    and null with the gate off and a reranker other than Jev.
- [x] 1.2 Have `SearchDocumentsTool` put the summary under `_meta["maf-lab/relevance"]` whether or not the trace flag
  is set.
  - Verify with a test:
    - The summary is present without the flag, and no diagnostics are present.
    - The structured content matches a traced call.
    - The summary JSON holds no chunk id, doc id, query or snippet.

## 2. api: `relevance` trace event

- [x] 2.1 Add `TraceKinds.Relevance = "relevance"` and `TraceMeta.Relevance = "maf-lab/relevance"`.
  - In `ChatTurnRunner.TraceToolResult`, strip that key from the recorded raw result.
  - Emit a `relevance` event (summary + `callId`) after `tool.result`/`retrieval`, whose duration is `durationMs`.
  - The title names Jev, max vs floor, and kept / silenced / ungated with the reason.
  - When there is no summary but `retrieval.relevance` exists (an older mcp-retrieval), build the event from that.
  - Verify with `TurnTraceTests` covering:
    - a kept search;
    - a silenced search;
    - an unavailable judge;
    - `TraceRetrieval=false` (a `relevance` event and no `retrieval` event);
    - no judgment (no event);
    - the raw `tool.result` event does not contain the `maf-lab/relevance` key.
- [x] 2.2 Verify with a test that the data envelope handed to the model contains no relevance summary.

## 3. api: tool-result screening requests and wall-clock

- [x] 3.1 Time the `Guardrail.ScreenToolResultAsync` screening end to end, and count its non-empty items as `requests`.
  - Carry both on `ScreenedToolResult`.
  - Pass them through `Guardrail.Trace`, which gets optional `requests`/`elapsedMs`: duration = `elapsedMs`, and the
    title appends "· N Jev requests" when N > 1.
  - The reviewer check passes `requests: 1`.
  - Verify with `GuardrailTests`:
    - five excerpts → `requests` 5, the latency of each item kept, and a title mentioning 5 requests;
    - a single result → `requests` 1 and no count in the title;
    - an empty excerpt is not counted.

## 4. api: Jev statistics read the explicit records

- [x] 4.1 In `JevStatistics.Read`:
  - Take relevance facts from `relevance` events when a turn has any, and otherwise from `retrieval.data.relevance`.
    With a `relevance` event, `rerankedByJev` comes from the event; the fallback keeps `settings.reranker`.
  - Take content-screening requests from `requests`, falling back to the item count.
  - Verify with `JevStatsTests` covering:
    - a turn with both a `retrieval` and a `relevance` event, counted once;
    - an old turn with only `retrieval.relevance`, counted;
    - a turn with only a `relevance` event (diagnostics off), counted;
    - a screening with `requests` 3 and 5 items, counted as 3.
- [x] 4.2 Remove "partner" from the guardrail section's wording and contracts wherever it implies a count the
  endpoint never makes, and state the scope as chat turns in `JevStatsContracts` doc comments. Verify with
  `dotnet build` and the existing `JevStatsTests`.

## 5. web

- [x] 5.1 Add `RelevanceData` to `traceData.ts` and give `relevance` the retrieval-family colour in `kindColors.ts`.
  Verify with `npm run typecheck` (or `make lint`).
- [x] 5.2 Add a "Jev relevance" block to each search in `RetrievalTab`:
  - model, floor, max, kept / silenced / ungated with the reason, reranked-by-Jev, duration, and the probability of
    each chunk when the diagnostics carry it;
  - fall back to the `relevance` event with the same `callId`;
  - show relevance-only blocks when a turn has no `retrieval` events.
  - Verify with `MonitorPanel.test.tsx` cases: a silenced search with scores, and a judgment without diagnostics. Also
    verify a `relevance` row with its duration in the timeline.
- [x] 5.3 Add a scope line to `JevPage.tsx` ("Jev calls made by chat turns — the A2A partner path is not counted") and
  drop "partner" from the guardrail labels. Verify with a `JevPage.test.tsx` assertion for the scope text.

## 6. Docs and verification

- [x] 6.1 Update `docs/trace-events.md`:
  - add the `relevance` kind;
  - add `requests` to `guardrail`;
  - add `maf-lab/relevance` to the retrieval `_meta` notes;
  - add `relevance` to the typical order.

  Verify by reading the table against the emitted JSON from task 2.1.
- [x] 6.2 Run `make test` and `make lint`, and confirm both pass.
- [x] 6.3 Run `make`, then ask a procedural question on http://localhost:7171. Confirm the Behind-the-scenes timeline
  shows a `relevance` row with the Jev latency and the retrieval view shows the Jev relevance block. Then run with
  `Agent__TraceRetrieval=false` and confirm the `relevance` row still appears and `/admin/jev` counts the search.
