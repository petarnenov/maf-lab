# Design

## Context

- `JevClient` (`src/Maf.Lab.Retrieval/Jev/JevClient.cs`) calls `IHttpClientFactory.CreateClient("jev")` on every
  request. The factory rotates the primary handler every two minutes (its default), so the pooled connection is thrown
  away regularly and a request after a quiet period pays DNS + TCP + TLS again.
- `JevIntentClassifier` builds its own `JevClient` (`new(http, credential, options)`), and `JevGuard` has its own copy
  of the POST (same wire shape as `JevRequest`, its own `CreateClient` call). The relevance judge and the answer check
  already take the DI singleton.
- Nothing retries. The hosts set `System.Net.Http.HttpClient` logging to Warning, and the Jev client does not log the
  status code, so a 429/5xx is visible only as a `rejected (NNN)` reason on a trace, and a transport error only as a type
  name.
- Every caller races its request against its own budget (`Task.WhenAny` + a linked `CancellationTokenSource`), which
  cancels the request's token when the budget ends.
- The unit tests construct `HttpClient` + `JevAuthHandler` + `FakeJev` by hand and pass a one-client factory; the api
  host tests (`ApiFactory`) replace the named client's primary handler with `FakeJev` and count requests.

## Goals / Non-Goals

**Goals:**
- One `HttpClient` instance per process for Jev, on a handler that is never rotated by the factory, with explicit
  pooling/keep-alive settings.
- A background warm-up per host; a hand-written retry + per-attempt log handler in the Jev client's pipeline.

**Non-Goals:**
- No Polly / `Microsoft.Extensions.Http.Resilience` (a new package would need a DECISIONS entry for a few dozen lines of
  code; the budget race already owns timeouts).
- No change to any caller's budget, failure reasons, trace events or the Jev statistics.
- No new metrics; the OTel HttpClient instrumentation already emits a span per attempt.

## Decisions

1. **Keep the named client, pin its handler, cache the instance.** `AddJevClient` keeps `AddHttpClient("jev")` (so the
   auth handler, OTel instrumentation and the tests' `ConfigurePrimaryHttpMessageHandler(() => FakeJev)` still apply),
   adds `ConfigurePrimaryHttpMessageHandler` with a `SocketsHttpHandler` (`PooledConnectionLifetime` from options,
   default 10 min, for DNS; `PooledConnectionIdleTimeout` 5 min; HTTP/2 keep-alive pings every 30 s while idle;
   `EnableMultipleHttp2Connections`) and `SetHandlerLifetime(Timeout.InfiniteTimeSpan)`. `JevClient` calls
   `CreateClient` once in its constructor and keeps the `HttpClient`. *Alternative:* a static `HttpClient` field — rejected,
   it bypasses the auth handler registration and the test seam.

2. **One `JevClient` for every caller.** `JevIntentClassifier` and `JevGuard` take the `JevClient` singleton instead of
   `IHttpClientFactory`; the guard's POST is replaced by `JevClient.AskAsync` with its own timeout, mapping the outcome to
   `GuardScores` exactly as before (same failure strings). `JevGuardRequest` goes away (identical to `JevRequest`).

3. **Retry + logging as one `DelegatingHandler` (`JevRetryHandler`), outermost in the pipeline.** Outside
   `JevAuthHandler`, so each attempt is authorised and each attempt is its own OTel span. Transient = no response
   (`HttpRequestException`), 408, 429, or ≥ 500 except 501/505. Delay = `RetryDelayMs × 2^(n-1)` with ±20 % jitter, or
   the `Retry-After` delta when the server sends one; the delay awaits the request's token, so when the caller's budget
   ends the delay throws and no further attempt is made (the caller's race already returns at its timeout). The failed
   response is disposed before the retry; the buffered `StringContent` is safe to resend. Defaults: `MaxRetries` 1,
   `RetryDelayMs` 100 — with a 2 s classification budget one quick retry fits, more would not. *Alternative:* retry
   inside `JevClient.AskAsync` — rejected, the attempt-level status code and timing are only visible below the client.

4. **Log lines.** Category `Maf.Lab.Retrieval.Jev.JevRetryHandler` (not `System.Net.Http.*`, which the hosts silence).
   `Jev attempt {Attempt}/{MaxAttempts} → {StatusCode} in {DurationMs} ms` (Information on success, Warning otherwise),
   `Jev attempt {Attempt}/{MaxAttempts} failed: {Error} after {DurationMs} ms` (Warning, type name only, no message or
   stack), and `Jev retrying after {Reason} in {DelayMs} ms` (Warning). Nothing from the body; the key never passes
   through this handler's log calls (it only exists in the header the inner handler sets).

5. **Warm-up as a `BackgroundService` (`JevWarmup`) registered by `AddJevClient`.** It waits for
   `IHostApplicationLifetime.ApplicationStarted` (never delaying readiness), then sends one System One request through
   `JevClient.AskAsync` with fixed state `{ "text": "warm-up" }` and one Noul, bounded by `WarmUpTimeoutSeconds`
   (default 5 — a cold TLS + model hop is slower than a turn's budget, and nobody waits on it). It logs one line with
   the model or the failure reason and the duration; the attempt lines above carry the status code. Skipped (debug log)
   when the key is missing or `WarmUp` is false. A real System One request rather than `GET /`: it exercises the same
   path, the auth header and the model a turn will use, and the CI stub serves it. It goes through `JevClient` directly,
   so no turn trace records it and the Jev statistics are untouched.

6. **Test hosts.** `ApiFactory` sets `Jev:WarmUp=false` so request-counting tests stay exact; dedicated tests cover
   warm-up. Hosts without a key (portfolio/retrieval factories in tests) skip it by rule.

## Risks / Trade-offs

- [A retry doubles load on an overloaded Jev (429/529)] → one retry by default, honours `Retry-After`, budget-bounded,
  configurable to 0.
- [Warm-up costs one Jev request per process start (api + 2 × retrieval + portfolio)] → negligible vs. turns; `WarmUp`
  switch.
- [An infinite handler lifetime would pin DNS] → `PooledConnectionLifetime` recycles connections instead.
- [Retry of a non-idempotent POST] → System One is a pure read (classification); resending has no side effect.
- [Tests that count Jev requests with an error status through the api host now see the retry] → adjust those counts
  or set `Jev:MaxRetries` for the test explicitly.

## Migration Plan

Configuration-only defaults; no data migration. Rollback: `Jev__WarmUp=false`, `Jev__MaxRetries=0` restore the old
wire behaviour without a redeploy of code.
