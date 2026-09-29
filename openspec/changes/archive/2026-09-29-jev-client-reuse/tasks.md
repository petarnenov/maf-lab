# Tasks

## 1. Shared kept-alive client

- [x] 1.1 Add `WarmUp`, `WarmUpTimeoutSeconds`, `MaxRetries`, `RetryDelayMs`, `PooledConnectionLifetimeMinutes` to `JevOptions` with the design's defaults; verify the solution builds
- [x] 1.2 In `AddJevClient`, give the named client a `SocketsHttpHandler` with pooling/keep-alive settings and an infinite handler lifetime; make `JevClient` create its `HttpClient` once and reuse it; verify with a unit test that two requests use one `CreateClient` call
- [x] 1.3 Make `JevIntentClassifier` and `JevGuard` take the `JevClient` singleton (guard maps `JevOutcome` to `GuardScores` with the same failure strings; drop `JevGuardRequest`); verify the existing classifier, routing, portfolio, guardrail and answer-check tests pass

## 2. Retries and per-attempt logging

- [x] 2.1 Add `JevRetryHandler` (transient = transport error, 408, 429, 5xx except 501/505; exponential delay with jitter or `Retry-After`; delay bound by the request token; logs every attempt with status code / error type and duration, and every retry with reason and delay) and register it outermost on the named client; verify with unit tests: 503→200 retried, 401 not retried, 529 exhausted after 1+N attempts, cancelled delay sends no further attempt, log lines carry status codes and no body or key

## 3. Warm-up

- [x] 3.1 Add `JevWarmup` background service registered by `AddJevClient`: after `ApplicationStarted`, one fixed-text System One request via `JevClient`, bounded by `WarmUpTimeoutSeconds`, one outcome log line; skipped without a key or with `WarmUp=false`; verify with unit tests for sent-once, skipped-without-key, skipped-when-disabled and failure-does-not-throw
- [x] 3.2 Set `Jev:WarmUp=false` in `ApiFactory` and adjust any api-host test whose Jev request count changes because of the retry; verify `dotnet test tests/Maf.Lab.Tests` is green

## 4. Docs and verification

- [x] 4.1 Document the new `Jev:*` settings (appsettings / README or DECISIONS entry for the retry and warm-up defaults); verify `openspec validate jev-client-reuse --strict` passes
- [x] 4.2 Run the full unit test suite and `openspec validate --all --strict`; both green
