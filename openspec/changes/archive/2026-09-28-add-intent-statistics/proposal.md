# Proposal

## Why

Every chat turn now asks Jev two questions — which intent, and whether the question is about the billing domain — and
the answer decides whether retrieval is forced. What Jev answered is recorded per turn in the `intent` trace event
(choice, probabilities, confidence, in-domain probability, model version, latency, and why an answer was not used), but
it can only be read one turn at a time in the monitor. Nobody can see how Jev behaves in aggregate: how often the
confidence floor or the domain gate overrules it, how its confidences are spread against the 0.5 and 0.2 floors, or
how often it is too slow. That last one is not hypothetical: TypeSafe was intermittently degraded today (calls of
22–36 s and 503s), and every such turn silently lost its forced retrieval with `timed out after 2s` in a trace nobody
looked at. The offline `intent` eval's history has the same problem — it sits in report files.

## What Changes

- A new endpoint, `GET /api/admin/intent-stats?window=1h|24h|7d`, returns aggregates of the caller's firm's `intent`
  trace events: outcome counts (used, gated, failed) with reasons, the forced-retrieval rate, Jev's choice against the
  intent the turn proceeded with, confidence and in-domain histograms, a confidence × in-domain point set (numbers only),
  mean probability per intent, latency percentiles and histogram against the timeout, counts over time, and the model
  versions that answered. Only FIRM_ADMIN may call it; the firm comes from the token. No question or answer text is
  returned, and nothing about it is logged.
- A new screen, `/admin/intents` ("Jev intents"), draws those aggregates as charts, the configured floors on each of
  them, a diagram of the classification pipeline with the live floors, and the history of the `intent` eval (accuracy
  per language and per split, forcing rates, the latest run's failures) from the existing eval report endpoint.
- Charts are hand-built inline SVG React components; no charting dependency is added.

## Capabilities

### New Capabilities

- `intent-statistics`: aggregated statistics of the intent classifier's answers for one firm, over a chosen window, as
  a purpose-built response with no message content.

### Modified Capabilities

- `web-ui`: a new "Intent statistics screen" requirement (added; no existing requirement changes).

## Impact

- `src/Maf.Lab.Api/Endpoints/IntentStatsEndpoints.cs` (new), `src/Maf.Lab.Api/Agent/IntentStatistics.cs` (new, the
  aggregation), `src/Maf.Lab.Domain/Intent/IntentStatsContracts.cs` (new DTOs), `Program.cs` (mapping).
- `web/src/intents/` (new page, chart components, CSS module), `web/src/App.tsx`, `web/src/components/Layout.tsx`,
  `web/src/api/types.ts`.
- Tests: `tests/Maf.Lab.Tests/IntentStatsTests.cs`; Vitest tests beside the page.
- `DECISIONS.md` — a short entry: why no chart library, what counts as used/gated/failed.
- No package added or moved. Reads only traces, so the window is bounded by trace retention (7 days).
