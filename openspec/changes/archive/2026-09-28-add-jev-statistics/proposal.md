# Proposal

## Why

When the "Jev intents" screen (`add-intent-statistics`, §34) was built, Jev answered one question per turn: the
intent. Since then Jev has spread to four more places — it screens the prompt, every tool result and other agents'
words (the guardrail, §35); it judges whether a search answers and reorders it (the passage-relevance gate and Jev
reranker, §36); and it pre-routes read tools on data turns (§37). The statistics screen still reads only the `intent`
trace event, so most of what Jev now does — and, more importantly, how often it is unavailable across *all* of those
places — cannot be seen in aggregate. That last point is not hypothetical: TypeSafe was intermittently degraded
(22–36 s calls, 429/503) while these changes landed, and every site fails in its own way (intent forces nothing, a
tool result is withheld or fails open, a search is left ungated). Each such failure sits in a trace nobody opened.

## What Changes

- A new endpoint, `GET /api/admin/jev-stats?window=1h|24h|7d`, returns aggregates of every Jev call site for the
  caller's firm, computed on the server from the turn traces already stored (`intent`, `guardrail`, `retrieval`
  relevance diagnostics, and the `model.request` count per turn). FIRM_ADMIN only; the firm comes from the token;
  numbers only, no message content, no identifiers. It embeds the existing intent aggregate (reusing its aggregation)
  and adds guardrail, passage-relevance/reranker and tool-routing sections plus a cross-cutting Jev
  requests/latency/availability view.
- The screen is broadened from "Jev intents" (`/admin/intents`) into a single **"Jev"** overview (`/admin/jev`) with a
  cross-cutting availability section on top and a section per call site below: intent (unchanged charts), guardrail,
  relevance & rerank, and tool routing. `/admin/intents` redirects to `/admin/jev` so existing links still work.
- No new trace field is added: every number comes from data the traces already record (confirmed against
  `Guardrail`/`JevGuard`, `DocumentSearchService`/`SearchDiagnostics`, `ChatTurnRunner`'s routing and `tool.forced`
  emission, and `TracingChatClient`'s `model.request` events). Only events whose model is `jev-*` are counted.
- The existing `GET /api/admin/intent-stats` endpoint stays, unchanged and backward-compatible; the Jev overview
  composes the same aggregation rather than replacing it.
- Charts stay hand-built inline SVG; no charting dependency is added.

## Capabilities

### New Capabilities

- `jev-statistics`: aggregated statistics of every Jev call site (intent, guardrail, passage-relevance/reranker, tool
  routing) and a cross-cutting requests/latency/availability view, for one firm over a chosen window, as a
  purpose-built response with no message content.

### Modified Capabilities

- `web-ui`: the "Intent statistics screen" requirement becomes the broader "Jev statistics screen" — the same page,
  renamed and moved to `/admin/jev`, with the new sections. Its existing scenarios are kept.
- `intent-statistics`: the intent aggregate is now also delivered embedded in the Jev overview; the standalone
  endpoint and its behaviour are unchanged. One scenario is added to record the embedding; every existing scenario
  is kept.

## Impact

- `src/Maf.Lab.Domain/Jev/JevStatsContracts.cs` (new DTOs), `src/Maf.Lab.Api/Agent/JevStatistics.cs` (new
  aggregation, reuses `IntentStatistics`), `src/Maf.Lab.Api/Endpoints/JevStatsEndpoints.cs` (new endpoint),
  `Program.cs` (mapping). `IntentStatistics.cs`/`IntentStatsEndpoints.cs` unchanged.
- `web/src/jev/` (new page and sections reusing the existing chart primitives), `web/src/App.tsx`,
  `web/src/components/Layout.tsx`, `web/src/api/types.ts`; the existing `web/src/intents/` charts, scales and CSS are
  reused.
- Tests: `tests/Maf.Lab.Tests/JevStatsTests.cs` (aggregation + firm scoping + no-content + admin-only); Vitest tests
  beside the new page. `IntentStatsTests.cs` unchanged.
- `docs/trace-events.md` — document the already-emitted `intent.routing` and `retrieval.relevance` sub-objects and
  the guardrail fields the aggregation reads (the doc is stale relative to §35–§37); no emission changes.
- `DECISIONS.md` — a short entry (§38): one Jev overview, what counts as a Jev request per site, why no new trace
  field. No package added or moved. Reads only traces, so the window is bounded by trace retention (7 days).
