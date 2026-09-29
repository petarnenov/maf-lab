# Proposal

## Why

Jev now sits on five paths of a chat turn: the intent request (with screening, domain and routing), tool-result
screening, the relevance judge in each retrieval server, and the answer check. Each one fails open on its own budget
(2–3 s), and nothing remembers that the previous call just failed. On 2026-09-28 TypeSafe answered in 22–36 s or with
503. During an outage like that, every turn waits out each site's timeout in turn. A turn with two searches and two tool
results adds up to about 13 s for no benefit, because every one of those calls ends in the same fail-open outcome.

## What Changes

- Every process that talks to Jev keeps one circuit breaker in front of all its Jev requests.
  - After a configured number of consecutive transient failures, Jev is skipped for a cool-down period:
    - transient failures are timeouts, transport errors, 408, 429 and 5xx;
    - while skipped, calls return at once with the reason `circuit open` and send nothing.
  - After the cool-down, one real request goes through as a probe:
    - if it succeeds, the circuit closes;
    - if it fails, the circuit opens again.
- **Every call site treats `circuit open` exactly like its existing "Jev unavailable" outcome.**
  - Prompts, tool results, partners, searches and answers still fail open.
  - A reviewer's words still fail closed.
  - No site gains a new behaviour.
- The breaker never counts these as failures, and never hides them:
  - a missing key;
  - a caller's own cancellation;
  - a non-transient rejection, such as 400, 401, 403 or 422.
- State changes (opened, probing, closed) are logged once each. The log line carries counts and reasons, no content.
- The Jev statistics and the `/admin/jev` screen count a skipped call as *skipped*, not as a request.
  - A skipped call is kept apart from the unavailable requests, per site and over time.
  - This way an outage stays visible after the breaker opens, instead of looking like quiet traffic.
- DECISIONS records the breaker's defaults. It also records the measured Jev requests per turn, the cost the breaker
  bounds.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `jev-client`: adds the circuit breaker in front of every Jev request in a process: when it opens, what it counts, the
  half-open probe and state-change logging.
- `jev-statistics`: counts calls skipped by an open circuit, per site, in the overview and over time, apart from
  requests and unavailability.
- `web-ui`: the Jev statistics screen shows skipped calls next to unavailable requests.

## Impact

- Code:
  - `src/Maf.Lab.Retrieval/Jev/`: the breaker, `JevClient`, `JevOptions` and the registration.
  - Call sites record `requests: 0` on a skip:
    - `src/Maf.Lab.Api/Agent/Jev/JevGuard.cs` and `Agent/Guardrail.cs`;
    - `Agent/Jev/JevAnswerCheck.cs`;
    - the relevance judge in `src/Maf.Lab.Retrieval/Rerank/`.
  - `src/Maf.Lab.Api/Agent/JevStatistics.cs` and `src/Maf.Lab.Domain/Jev/JevStatsContracts.cs`.
  - `web/src/jev/JevPage.tsx` and `web/src/api/types.ts`.
- Hosts: api, both mcp-retrieval replicas and mcp-portfolio each hold their own breaker. There is no shared state.
- Configuration: new `Jev:Breaker:FailureThreshold`, `Jev:Breaker:OpenSeconds`. A threshold of 0 disables the breaker.
- API: `GET /api/admin/jev-stats` gains `skipped` fields. The change is additive, and existing fields keep their meaning.
- Dependencies: none added.
