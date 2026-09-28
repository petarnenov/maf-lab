# Tasks

## 1. Aggregation

- [x] 1.1 Add the response DTOs (`JevStatsReport`, `JevOverview`, `JevSiteSummary`, `JevAvailabilityBucket`,
      `GuardrailStats` + parts, `RelevanceStats` + parts, `RoutingStats` + parts, and a `JevStatsSettings`) in
      `src/Maf.Lab.Domain/Jev/JevStatsContracts.cs`, reusing `IntentStatsReport`/`IntentLatency` from
      `Maf.Lab.Domain/Intent`. Verify `make lint` builds clean.
- [x] 1.2 Add `JevStatistics.Aggregate(rows, window, settings, now)` in `src/Maf.Lab.Api/Agent/JevStatistics.cs`: a
      pure function that calls `IntentStatistics.Aggregate` for the intent section and parses each trace once for the
      `guardrail`, `retrieval` (`relevance`) and `model.request` events, producing the guardrail, relevance, routing
      and cross-cutting overview sections per the design. Reuse `IntentStatistics.Windows`, `Percentile`,
      `Classify` (for intent unavailability) and the latency-bin helper. Count a Jev request only for jev-model
      intent events, content-guard items and judged searches; count unavailability per site; sum both into the
      overview timeline.
- [x] 1.3 Unit tests in `tests/Maf.Lab.Tests/JevStatsTests.cs` fed synthesised traces of the exact shape each site
      writes: a guardrail event's decisions and tripping question are counted; a `tool_result` item and a judged
      search each count as one request while a `prompt` screening and a routing answer count as none; a silenced
      search is `gated` and a `relevance.reason` search is `unavailable`, distinguished; routed vs unrouted data
      turns' model-call medians come from `model.request` counts; the overview timeline sums unavailability across
      sites; a non-Jev intent event is excluded; empty input gives zeros and empty distributions. Verify they pass.

## 2. Endpoint

- [x] 2.1 Map `GET /api/admin/jev-stats` in `src/Maf.Lab.Api/Endpoints/JevStatsEndpoints.cs` under the `firm-admin`
      policy, scoped to `principal.FirmId`, window from `1h|24h|7d` (default `24h`), reading `TurnTraces` for the firm
      and window and passing `IOptions<JevOptions>` and `IOptions<GuardOptions>` into the aggregation for the settings
      block; register it in `Program.cs`. Leave `IntentStatsEndpoints`/`IntentStatistics` unchanged. Verify API tests
      through `ApiFactory`: turns driven with `FakeJev` (used/gated/failed intents, a blocked prompt and a withheld
      tool result, a silenced search, a routed data turn) populate the right sections; a firm-b admin sees none of
      firm-a's turns; an ADVISOR/OPS gets 403; `window=30d` gets a validation problem; the question text appears
      neither in the body nor in the logs.

## 3. Screen

- [x] 3.1 Add the report types to `web/src/api/types.ts` and, under `web/src/jev/`, the `JevPage` (`/admin/jev`, inside
      `RequireAdmin`, a "Jev" navigation link) with the overview section, the guardrail, relevance-and-rerank and
      routing sections, and the intent section reusing the existing intent charts (lift the intent rendering into a
      reusable component), plus the intent eval history. Reuse `web/src/intents/charts.tsx`, `scale.ts`,
      `PipelineDiagram` and the CSS module; add `/admin/intents` → `/admin/jev` redirect in `App.tsx` and rename the
      `Layout` nav link to "Jev". Verify `make lint` (eslint/prettier/tsc) is clean.
- [x] 3.2 Vitest tests beside the page: the overview and every section render from a fixture with their charts and
      floors; the availability-over-time chart and per-site table show; the no-data and error states hold; the routing
      model-calls comparison and relevance floor render; an ADVISOR sees access denied; `/admin/jev` is reached from
      the nav and `/admin/intents` redirects to it. Keep or update the existing intent page/chart Vitest tests for the
      moved component. Verify they pass.

## 4. Record and verify

- [x] 4.1 Update `docs/trace-events.md` to document the already-emitted `intent.routing` and `retrieval.relevance`
      sub-objects and the current `guardrail` fields the aggregation reads (documentation only, no emission change).
      Add a DECISIONS.md entry (§38): one Jev overview page/endpoint, what counts as a Jev request per site, why no new
      trace field. Verify both read alongside §34–§37.
- [x] 4.2 Run `make lint`, `make test` and `make specs`; all pass. `openspec validate add-jev-statistics --strict`
      passes.
- [x] 4.3 Rebuild with `make up`, drive real chat turns through the load balancer (procedural, data-for-routing,
      off-domain-for-gate-silencing-and-guardrail, and an injection attempt for a guardrail block), then check
      `GET /api/admin/jev-stats` with curl and `/admin/jev` in a browser (screenshot light + dark) — every new
      section populates with the turns just made. Note any Jev unavailability seen; it is data for the availability
      view, not a failure.
