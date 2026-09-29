# Design

## Context

- **`JevClient.AskAsync`** (`src/Maf.Lab.Retrieval/Jev/JevClient.cs`) is the one seam every caller goes through:
  - callers: intent classifier, `JevGuard` (prompt, content, partner, reviewer), `RelevanceJudge`, `JevAnswerCheck`
    and `JevWarmup`;
  - it is a singleton per process and never throws;
  - it returns `JevOutcome(Response?, Failure?, DurationMs)`;
  - failure strings: `no key`, `timed out after Ns`, `rejected (NNN)`, or an exception type name.
- **`JevRetryHandler`** sits below the client, inside the `HttpClient` pipeline. It retries transient statuses within
  the caller's budget, so `AskAsync` only ever sees the final outcome of a call.
- **Every caller already maps "no answer" to its fail-open or fail-closed outcome** and records the failure string as
  its reason. The one exception is the reviewer's words, which fail closed via `Guardrail`.
  - Intent: `IntentStatistics.Classify` turns an unknown reason into `Failed` under its own label.
  - Relevance: any reason marks the search unavailable.
  - Answer check: `unchecked` plus `requests`.
- **The statistics count requests per site from trace events** (`JevStatistics.cs`):
  - intent: one request per Jev intent event;
  - content screening: `Requests`, which is computed as the number of non-empty texts, not the number actually sent;
  - relevance: one request per judged search;
  - answer: the `requests` field.
- **Processes that call Jev:** api, mcp-retrieval ×2 behind nginx, and mcp-portfolio, which shares the retrieval core.

## Goals / Non-Goals

**Goals:**
- A skipped call costs about 0 ms and sends nothing, and every site's behaviour is unchanged apart from latency.
- A skip can be told apart in traces and statistics: the reason is `circuit open` and 0 requests are recorded.

**Non-Goals:**
- No change to the budgets, the retry policy or `Retry-After` handling. The breaker opens for a fixed period and does not
  read `Retry-After`: the retry handler consumes that header today, and a fixed period is easier to reason about.
- No shared, cross-process breaker.
- No health-check or readiness change. An open circuit is not an unhealthy service, because every site works without Jev.
- No new metrics instrument. The state-change logs and the trace reasons are enough for this lab. An OTel counter can
  follow.
- No fallback classifier while the circuit is open. The only question here is how long we wait before failing open.

## Decisions

1. **The breaker lives inside `JevClient.AskAsync`, not in the `HttpClient` pipeline.**
   - It must see the caller's outcome: a budget timeout happens in the `Task.WhenAny` race above the handlers, and a
     handler never sees it.
   - Skipping in `AskAsync` also avoids even building the request.
   - *Alternative:* a `DelegatingHandler` that throws when open. Rejected: it cannot see timeouts, and it would surface
     as an exception type name instead of a clear reason.
   - Implementation: a small `JevCircuitBreaker` singleton (state, consecutive failures, `openedAt`, a probe-in-flight
     flag, a skipped counter), guarded by one `lock`, with time from `TimeProvider` so tests are fast. It is injected
     into `JevClient` and registered by `AddJevClient`.

2. **What counts as a failure is decided from the outcome, not from strings everywhere.**
   - `JevClient` classifies at the point where it creates each outcome:
     - timeout, transport exception and final 408/429/5xx (the same set as `JevRetryHandler.IsTransient`) are failures;
     - a 2xx with a body is a success;
     - `no key`, caller cancellation (which rethrows today) and other 4xx are neutral.
   - The mapping reuses `IsTransient`, so the retry policy and the breaker cannot drift apart.

3. **A skipped outcome is marked explicitly.**
   - `JevOutcome` gains `bool Skipped`, and `Failure = "circuit open"`, `DurationMs = 0`.
   - Callers need no new branch, because they already treat `Response == null` as unavailable. They only record their
     request count:
     - `JevAnswerCheck` passes `requests: outcome.Skipped ? 0 : 1`;
     - `Guardrail`'s content screening counts requests from the outcomes it received (items not skipped), not from
       the number of non-empty texts;
     - intent and relevance events carry no request count. The statistics recognise the `circuit open` reason there.
   - *Alternative:* a `requests` field on the intent and relevance events. Rejected: older events lack it, and the
     reason is already recorded and unambiguous.

4. **Half-open means a single probe, the caller's own call.**
   - The first call after the open period is sent with its normal budget, and the others skip until it resolves.
   - No synthetic probe request: a real call is the probe, and nothing runs in the background.
   - The open period is fixed, not exponential. Outages here last minutes to hours, and a 30 s period costs at most one
     timeout per 30 s per process.

5. **Defaults: `Jev:Breaker:FailureThreshold` = 3, `Jev:Breaker:OpenSeconds` = 30. A threshold of 0 disables it.**
   - Three consecutive failures is under one degraded turn (intent + search + screening + check), so a second turn
     never waits.
   - Isolated timeouts are rare in the eval traces: 0 timeouts in about 1000 relevance requests and 240 classified
     turns (DECISIONS §36/§37). Three in a row therefore signals an outage, not noise.
   - Concurrent failures from parallel turns count toward the same streak. That is intended: they are simultaneous
     evidence of the same outage.
   - **`ApiFactory` and the unit tests that script Jev failures set the threshold to 0.** This keeps their request
     counts as they are. The breaker has its own tests.

6. **The warm-up feeds the breaker like any call.** A failed warm-up with a 5 s budget is real evidence. It adds one
   failure, so a later real call may open the circuit sooner. It cannot open the circuit alone unless the threshold is 1.

7. **Statistics: skipped calls are counted apart, and existing fields keep their meaning.**
   - `JevSiteSummary` and `JevAvailabilityBucket` gain `Skipped`, and `JevOverview` gains a total `Skipped`.
   - An intent event or relevance event whose reason is `circuit open` is excluded from `Requests`, `Unavailable` and
     latency, and included in `Skipped`.
   - Guard and answer events are read from their `requests` field, as today, plus skipped = items screened minus
     requests.
   - `IntentStatistics.Classify` needs no change: `circuit open` already falls into `Failed` with its own label.

8. **Logs.** Category `Maf.Lab.Retrieval.Jev.JevCircuitBreaker`:
   - `Jev circuit opened after {Failures} consecutive failures (last: {Reason}); skipping Jev for {OpenSeconds}s`
     (Warning);
   - `Jev circuit half-open: probing` (Information);
   - `Jev circuit closed after {OpenMs} ms; {Skipped} calls skipped` (Information).

   The reasons are our own failure strings, never a body. A skipped call does not log.

9. **The cost goes to DECISIONS.**
   - Read requests per turn and per-site p50/p90 from `/admin/jev`, or from the kept eval traces, over a
     `make eval SUITE=selection` and a `generation` run.
   - Record the worst-case wait per turn without the breaker, the wait with it, and the defaults above.

## Risks / Trade-offs

- **[A short blip opens the circuit and costs up to 30 s of Jev-less turns]** → The quality loss is bounded and
  visible: fail open is the same outcome the blip would have produced anyway. The threshold of 3 and a single probe keep
  it rare. Both knobs are configuration.
- **[Per-process state: each of the four processes learns about the outage separately]** → Each pays at most 3 timeouts
  before its breaker opens. Sharing state would put a network hop in front of every call, and each process's view of
  reachability is its own anyway.
- **[A slow but answering Jev (under budget) never opens the circuit]** → Intended. The breaker addresses waits that end
  in failure. Slow answers still show in the latency percentiles.
- **[Statistics readers expect `Requests` to include every call]** → The field keeps its meaning (requests sent). Skipped
  calls are an additive field, and the screen shows them next to unavailable requests.

## Migration Plan

- The change is additive. There is nothing to migrate in stored traces: older events have no `circuit open` reason and
  count exactly as before.
- Rollback: set `Jev__Breaker__FailureThreshold=0`, which restores the previous behaviour without a redeploy of code.
