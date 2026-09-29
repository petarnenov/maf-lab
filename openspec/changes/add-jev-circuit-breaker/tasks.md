# Tasks

## 1. Breaker core

- [x] 1.1 Add `JevBreakerOptions` (`Jev:Breaker:FailureThreshold` = 3, `Jev:Breaker:OpenSeconds` = 30; threshold 0 disables) under `JevOptions`; verify the solution builds and the options bind from `Jev__Breaker__*` in a unit test
- [x] 1.2 Add `JevCircuitBreaker` (closed/open/half-open, consecutive-failure count, single probe in flight, skipped counter, `TimeProvider` clock, one `lock`) with the state-change log lines of design §8; verify with unit tests: 3 timeouts open it, a success resets the count, a probe after the period closes or re-opens it, a second call during the probe is skipped, threshold 0 never opens, and a skipped call writes no log line
- [x] 1.3 Wire the breaker into `JevClient.AskAsync`:
  - an open circuit returns `JevOutcome(null, "circuit open", 0) { Skipped = true }` without building a request;
  - a timeout, transport error or final transient status (reuse `JevRetryHandler.IsTransient`) records a failure;
  - a 2xx with a body records a success;
  - `no key`, caller cancellation and other 4xx are neutral.

  Register the breaker in `AddJevClient`. Verify with `JevClientTests`, using `FakeJev` + a fake `TimeProvider`: the
  fourth call after 3 × 503 sends no request, 5 × 401 never opens, and no key never touches the breaker.
- [x] 1.4 Set `Jev:Breaker:FailureThreshold=0` in `ApiFactory` and in any unit test that scripts repeated Jev failures, so existing request counts hold; verify `dotnet test tests/Maf.Lab.Tests` is green

## 2. Call sites record a skip

- [x] 2.1 `JevAnswerCheck`: pass `requests: 0` when `outcome.Skipped`; verify with an `AnswerCheckTests` case: an open circuit makes the turn `unchecked` with reason `circuit open` and `requests: 0`
- [x] 2.2 `Guardrail` content screening: count `Requests` from outcomes that were actually sent, not from non-empty texts; verify with `GuardrailTests`: with an open circuit, a tool result is `Unscreened` (fail open) with 0 requests, and a reviewer's words are withheld (fail closed) with reason `circuit open`
- [x] 2.3 Intent classifier and relevance judge: confirm that `circuit open` reaches the trace as the recorded reason with no other change (intent → `Other`, nothing forced; search → ungated, fused order); verify with one `IntentClassifierTests` case and one relevance-judge test under an open circuit

## 3. Statistics and screen

- [x] 3.1 Add `Skipped` to `JevSiteSummary`, `JevAvailabilityBucket` and `JevOverview` (`src/Maf.Lab.Domain/Jev/JevStatsContracts.cs`); in `JevStatistics`:
  - an intent or relevance event with reason `circuit open` is excluded from requests, unavailable and latency, and
    counted as skipped;
  - guard skipped = screened items − requests;
  - answer skipped = `unchecked` + `circuit open` + `requests: 0`.

  Verify with `JevStatsTests`: the spec's 10-classifications case (7 requests / 2 unavailable / 3 skipped), the skipped
  answer check, a skipped search, and all-skipped-site latency = none.
- [x] 3.2 `web/src/api/types.ts` and `web/src/jev/JevPage.tsx`:
  - skipped count per site next to unavailable, plus the overview total;
  - a named "skipped (circuit open)" series in the requests/unavailability timeline, with hover values;
  - no series when the total is 0.

  Verify with `JevPage.test.tsx` cases for an outage and for none skipped, and run `npm test` in `web/`.

## 4. Docs, cost and verification

- [x] 4.1 Measure Jev requests per turn and the per-site p50/p90 from the kept traces of one `make eval SUITE=selection` and one `SUITE=generation` run (with `JEV_MAF_LAB`). Add a DECISIONS entry (next section number, "Jev circuit breaker") recording:
  - the numbers;
  - the worst-case wait per turn with and without the breaker;
  - the defaults and their reasoning (design §5);
  - the rejected alternatives: handler-level breaker, shared breaker, exponential open period, reading `Retry-After`.

  Document `Jev:Breaker:*` where the other `Jev:*` settings are documented. Verify that the entry exists and cites the
  measured run ids.
- [x] 4.2 Live check (`make`, then stop Jev reachability, e.g. `Jev__Endpoint` to an unroutable address on one api replica via `make dev`): the first turn pays at most 3 timeouts, the next turns add no Jev wait, the log shows one "opened" line, and `/admin/jev` shows skipped calls. After the endpoint is restored, one probe closes the circuit. Record the observed turn durations in the DECISIONS entry.
- [x] 4.3 Run `make test`, `make lint` and `openspec validate add-jev-circuit-breaker --strict`; all green
