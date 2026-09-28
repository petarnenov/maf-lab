# Tasks

## 1. Aggregation

- [x] 1.1 Add the response DTOs (`IntentStatsReport` and its parts) in `src/Maf.Lab.Domain/Intent/IntentStatsContracts.cs`
      per the design. Verify `make lint` builds clean.
- [x] 1.2 Add `IntentStatistics` in `src/Maf.Lab.Api/Agent/IntentStatistics.cs`: outcome and reason classification,
      Jev-only filter, buckets, histograms, points, means, nearest-rank percentiles, latency bins and pipeline counts,
      as a pure function of `(createdAt, trace json)` rows, window, settings and now. Verify unit tests in
      `tests/Maf.Lab.Tests/IntentStatsTests.cs` cover: every reason the classifier can produce maps to the right outcome
      and label; non-Jev events are excluded and counted; histogram and bucket placement; percentiles; empty input.

## 2. Endpoint

- [x] 2.1 Map `GET /api/admin/intent-stats` in `src/Maf.Lab.Api/Endpoints/IntentStatsEndpoints.cs` under the
      `firm-admin` policy, scoped to `principal.FirmId`, window from `1h|24h|7d` (default `24h`), and register it in
      `Program.cs`. Verify API tests through `ApiFactory`: turns driven with `FakeJev` (used, low confidence, outside the
      domain, timed out) are counted under the right outcomes; a firm-b admin sees none of firm-a's turns; an ADVISOR
      gets 403; `window=30d` gets a validation problem; the question text appears neither in the body nor in the logs.

## 3. Screen

- [x] 3.1 Add the report types to `web/src/api/types.ts` and chart components (inline SVG) with a CSS module under
      `web/src/intents/`, colour tokens for light and dark. Verify Vitest tests for the chart helpers (bins, scales,
      empty data) pass.
- [x] 3.2 Add `IntentStatsPage` (`/admin/intents`, inside `RequireAdmin`, a "Jev intents" navigation link) with the
      period picker, headline numbers, pipeline diagram, all charts from the spec, and the intent-eval history from
      `/api/evals/reports`. Verify Vitest tests: charts and floors render from a fixture; no-data state; error state;
      eval history and latest failures; an ADVISOR sees access denied.

## 4. Record and verify

- [x] 4.1 Add a DECISIONS.md entry (no chart library; outcome definitions; Jev-only filter). Verify it reads alongside
      §32–§33.
- [x] 4.2 Run `make lint`, `make test`, `make verify` and `make specs`; all pass.
- [x] 4.3 Rebuild with `make up`, drive real chat turns through the load balancer, and check `/api/admin/intent-stats`
      with curl and `/admin/intents` in a browser (screenshot) — the charts show the turns just made.
