# Proposal

## Why

Every Jev call sits on the critical path of a chat turn (classification before the first model call, screening, the
relevance judge, the answer check) with a budget of about two seconds. Today each call asks the client factory for a
fresh `HttpClient`, the intent classifier and the guard each build their own client wrapper, and the factory rotates
the underlying handler — so the first turn after start-up, and the first turn after each rotation, pays DNS, TCP and
TLS set-up out of that budget. When a call does fail, the logs say nothing about it: the host's `HttpClient` logging is
at Warning, the Jev client logs no status code, and a transient 429/5xx is simply the turn's loss.

## What Changes

- Every Jev request in a process goes through one long-lived HTTP client with a pooled, kept-alive connection, created
  once and reused by every caller (intent classifier, content guard, relevance judge, answer check).
  - The connection pool recycles connections on a fixed lifetime so a DNS change of the endpoint is still picked up.
- Each service that talks to Jev sends one warm-up request at start-up, in the background, so the first real turn
  finds an open connection.
  - It never delays or fails start-up; it is skipped when the key is missing or Jev is disabled; it can be switched
    off by configuration.
- The Jev client retries a transient failure (transport error, 408, 429, 5xx including 529) a bounded, configurable
  number of times, always inside the caller's existing budget; 4xx other than 408/429 are never retried.
- Every attempt is logged with structure only: attempt number, HTTP status code (or the transport error type),
  duration, and — for a retry — the reason and the delay before the next attempt. No request or response body, no
  question text, no key.

## Capabilities

### New Capabilities
- `jev-client`: how the services reach Jev at the transport level — one reused kept-alive connection, a start-up
  warm-up, bounded retries inside the caller's budget, and per-attempt logging of status codes and retries.

### Modified Capabilities
<!-- None: the callers' observable contracts (budgets, failure semantics, credential handling) are unchanged. -->

## Impact

- Code: `src/Maf.Lab.Retrieval/Jev/` (client, registration, new retry/logging handler, warm-up hosted service,
  options), `src/Maf.Lab.Api/Agent/Jev/JevIntentClassifier.cs` and `JevGuard.cs` (use the shared client instead of
  building their own).
- Hosts: api, mcp-retrieval and mcp-portfolio each run one warm-up at start-up (a single System One request).
- Configuration: new `Jev:WarmUp`, `Jev:MaxRetries`, `Jev:RetryDelayMs`, `Jev:PooledConnectionLifetimeMinutes`.
- Dependencies: none added (plain `SocketsHttpHandler` and a hand-written delegating handler; no Polly).
- Tests: new unit tests for reuse, retry, logging and warm-up; existing Jev tests keep passing.
