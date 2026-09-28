# Design

## Context

See proposal.md — Why. What exists today, confirmed against source:

- **Every Jev call site already writes what it did into the turn trace.** No emission changes are needed.
  - `intent` event (`ChatTurnRunner`, one per turn): `intent`, `forcedRetrieval`, `forcedTool`, `choice`,
    `probabilities`, `confidence`, `inDomain`, `model` (`jev-*`), `durationMs`, `reason`, and a nested `routing`
    object `{ tools:{tool:prob}|null, status, statusConfidence, routedTool, arguments, reason }` (null when routing
    is off). The intent request carries the prompt-guard and routing questions as fan-out, so they cost no separate
    request or latency (§35, §37).
  - `guardrail` events (`Guardrail.Trace`, zero or more per turn): `check` ∈ `prompt`, `partner_prompt`, `tool_result`,
    `reviewer`; `decision` ∈ `pass`, `blocked`, `withheld`, `unscreened`; `threshold`, `top`, `topQuestion`,
    `withheld` (count), `items:[{index,decision,scores:{questionId:prob}|null,durationMs,reason}]`, `model` (`jev-*`),
    `durationMs`, `reason`. Prompt/partner screenings ride in the intent (or partner) request; each `tool_result` /
    `reviewer` item is its own bounded Jev request.
  - `retrieval` events (`ChatTurnRunner` from MCP `_meta`, one per search): `settings.reranker` (`jev`|`llm`|null),
    `settings.relevanceGate`, and a `relevance` object `{ gate, floor, judged, max, silenced, model (jev-*),
    durationMs, reason, scores:[{chunkId,p}]|null }`. One search makes one Jev request, shared by the gate and the Jev
    reranker. `silenced=true` (with `reason=null`, `scores` present) is a gated search; `reason!=null` (`scores=null`,
    `max=null`) is Jev unavailable, search left ungated. Distinguishable in the trace.
  - `model.request` events (`TracingChatClient`, one per real provider iteration): counting them per turn gives the
    model calls a turn made. `TracingChatClient` sits below `RequiredToolModeChatClient`, so a forced or routed call is
    a `tool.forced` event, not a `model.request` — a routed data turn shows fewer `model.request` events than an
    unrouted one, which is how routing's saving is measured (§37).
- Traces live in `TurnTraces` (`FirmId`, `CreatedAt`, `Json` = the ordered event list), indexed by `CreatedAt`,
  deleted after 7 days by `TraceRetentionService`.
- `IntentStatistics.Aggregate(rows, window, settings, now)` already turns `(CreatedAt, Json)` rows into an
  `IntentStatsReport` as a pure function reading only the `intent` event. It is reused verbatim for the intent
  section. `IntentStatistics.Windows` and `IntentStatistics.Percentile` are reused for buckets and percentiles.
- `GET /api/admin/intent-stats` (FIRM_ADMIN, firm from token) and the `/admin/intents` "Jev intents" page and its
  hand-built SVG charts (`web/src/intents/charts.tsx`, `scale.ts`, `PipelineDiagram`, CSS module) exist and are
  reused. `GuardOptions` (section `Guard`) is bound in the api host (`AddJevIntentClassifier`).

## Goals / Non-Goals

**Goals:**

- One request per screen load returns every section, aggregated on the server, numbers only.
- Every aggregate is a pure function of the trace events that unit tests feed directly, like `IntentStatistics`.
- One coherent "Jev" page: a cross-cutting availability view on top, a section per call site below.
- No new trace field, no schema change, no new dependency.

**Non-Goals:**

- Changing what any turn records. Every metric is derived from existing trace data; where a metric could not be
  derived it is omitted rather than a field added.
- Replacing or changing the intent-stats endpoint or the intent charts. The Jev overview composes them.
- Per-passage relevance filtering stats, per-question probability scatters, or linking a chart to its turn (the trace
  endpoint would 404 for most). Numbers only.
- Statistics beyond trace retention (7 days); cross-firm or per-user breakdowns; server-side caching (revisit if the
  7-day query becomes slow).

## Decisions

### One new endpoint, composed from the same trace rows

`GET /api/admin/jev-stats?window=1h|24h|7d` reads `(CreatedAt, Json)` of the principal's firm's traces in the window
(the same query the intent endpoint uses) and returns a `JevStatsReport`. The intent section is produced by calling
`IntentStatistics.Aggregate` on the same rows; the guardrail, relevance, routing and overview sections are produced by
a new `JevStatistics.Aggregate` that parses each trace once with `JsonDocument`, reading only the `intent`,
`guardrail`, `retrieval` and `model.request` events and nothing else (no question, prompt or message text is touched).
Re-parsing the rows twice (once for intent, once for the rest) is accepted for clarity at a lab's volumes.

The standalone `intent-stats` endpoint stays for backward compatibility; the page uses only `jev-stats`.

### Jev-only, and what counts as a request

An event counts only when its `model` starts with `jev-` (intent, guardrail and relevance all record it). Non-Jev
intent events are reported as `excludedEvents`, exactly as the intent section already does.

A **Jev request** is counted once per: `intent` event (jev), each `tool_result`/`reviewer` guardrail *item*, and each
`retrieval` event whose `relevance` object shows a request was attempted (relevance present). A `prompt`/
`partner_prompt` guardrail event and a routing answer are *not* separate requests — they ride inside the intent (or
partner) request — so they add 0 to the request total (their outcomes are still shown in their sections). The three
request-bearing sites — **intent**, **guardrail** (content), **relevance** — are the overview's per-site rows and its
availability timeline.

### Unavailability, per site

- intent: `reason` in the failed set (`timed out…`, `rejected (NNN)`, `no answer`, `no key`, `classification
  disabled`, an exception name) — reused from `IntentStatistics.Classify`.
- guardrail: an item whose `decision` is `unscreened`.
- relevance: a `retrieval` event whose `relevance.reason` is non-null (`scores` null).

The overview timeline sums requests and unavailable across the three sites per time bucket — the degraded-Jev trend
the change is for.

### Response shape

New DTOs in `Maf.Lab.Domain/Jev/JevStatsContracts.cs`:

- `JevStatsReport { window, from, to, bucketMinutes, overview, intent (IntentStatsReport), guardrail, relevance,
  routing }`.
- `overview` `{ settings{model, guardEnabled, promptBlockAt, contentWithholdAt, crossTenantAt, relevanceFloor},
  requests, unavailable, turns, requestsPerTurn, sites:[{site, requests, unavailable, p50Ms, p90Ms}],
  timeline:[{start, requests, unavailable}] }`. `relevanceFloor` is read from the recorded `relevance.floor` (the
  running mcp-retrieval value), guard thresholds from `IOptions<GuardOptions>` in the api host.
- `guardrail` `{ checks:[{check, total, pass, blocked, withheld, unscreened}], trippedBy:[{question, decision,
  count}] (blocked/withheld deciding questions), blocked, withheld, unscreened, latency (IntentLatency over content
  items), timeline:[{start, screened, blocked, withheld, unscreened}] }`.
- `relevance` `{ floor, searches, gated, reranked, unavailable, maxHistogram:[{from,to,kept,gated}], latency
  (IntentLatency over relevance.durationMs), timeline:[{start, searches, gated, unavailable}] }`.
- `routing` `{ enabled, dataTurns, routed, tools:[{tool,count}], notRoutedReasons:[{reason,count}],
  modelCallsRoutedMedian, modelCallsUnroutedMedian, latency (IntentLatency over routed data turns' intent duration) }`.

`IntentLatency`, `IntentLatencyBin` and the window/bucket/percentile helpers are reused from the intent contracts and
`IntentStatistics`. Buckets: 5 minutes for `1h`, 1 hour for `24h`, 6 hours for `7d` — same as intent.

### Model calls saved by routing

For each trace with a *used* `data` intent, count its `model.request` events. The median over routed data turns
(`routing.routedTool != null`) against unrouted data turns is reported. Both come straight from the trace; no field is
added. Where there are no data turns of a kind, the median is null and the screen reads "no data".

### The page

`/admin/jev` ("Jev" in the nav) fetches `jev-stats` once and renders: the **Overview** (headline requests/
unavailability/requests-per-turn, an availability line over time, a per-site table), then **Intent** (the existing
intent charts, fed from `report.intent`), **Guardrail**, **Relevance & rerank**, **Routing**, and the intent eval
history. The existing chart primitives (`Columns`, `Bars`, `Lines`, `Scatter`, `Legend`, `PipelineDiagram`), scales
and CSS module are reused; the intent charts are lifted into a reusable section component so both the value and the
tests carry over. `/admin/intents` redirects to `/admin/jev`.

Colours follow the established three categorical slots (used/kept = blue, gated/withheld = orange, failed/unavailable =
aqua) already validated for colour-vision deficiency on both surfaces; every multi-series chart has a legend and hover
values, availability charts carry a table.

### Documentation

`docs/trace-events.md` is stale relative to §35–§37 (it omits `intent.routing`, `retrieval.relevance` and the current
guardrail fields). It is corrected to document the fields this aggregation reads. This is documentation of existing
behaviour, not a change to the turn-tracing contract, so no `turn-tracing` spec change is needed.

## Risks / Trade-offs

- [Parsing up to 7 days of trace JSON twice per request] → traces are capped at 1 MB and a lab has hundreds of turns;
  the query reads two columns through the `CreatedAt` index, filtered by firm; the page refreshes every 60 s. If slow:
  fold the second pass into `IntentStatistics` or store per-turn Jev counters at turn end.
- [Aggregates reveal a firm's usage volume and timing] → FIRM_ADMIN only, own firm only, no firm parameter, tested.
- [Trace field names are the contract] → the field reads are covered by unit tests that feed synthesised traces of the
  exact shape each site writes, and by an end-to-end test that drives real turns through the api.
- [A new guardrail question id or a new intent reason appears] → question ids are counted as data (a new one shows as
  its own row), and intent reasons reuse `IntentStatistics.Classify` (an unrecognised reason is a visible failure).

## Migration Plan

Additive: a new endpoint and a broadened screen, no schema change. `/admin/intents` redirects to `/admin/jev`; the
intent-stats endpoint stays. Rollback is removing the new endpoint and page and restoring the nav link.
