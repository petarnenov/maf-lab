# Design

## Context

See proposal.md — Why. What exists today:

- `ChatTurnRunner` writes one `intent` trace event per turn with `intent`, `forcedRetrieval`, `forcedTool`, `choice`,
  `probabilities` (per intent), `confidence`, `inDomain`, `model`, `durationMs` and `reason`. `JevIntentClassifier`
  produces the reasons: null (used); `low confidence (0.xx)`, `outside the domain (0.xx)`, `answer is not one of the
  known intents` (Jev answered, code did not act); `timed out after 2s`, `rejected (NNN)`, `no answer`, `no key`,
  `classification disabled`, or an exception type name (no answer at all). Failed calls still record `model` (the
  configured one) and, except for no key / disabled, `durationMs`.
- Traces live in `TurnTraces` (`FirmId`, `CreatedAt`, `Json` = the ordered event list), indexed by `CreatedAt`, deleted
  after 7 days by `TraceRetentionService`. The live database also holds events from the two earlier classifiers
  (rules with no model, `gemma4:31b`, `gpt-oss:120b`).
- Firm-wide admin screens (`/admin/feedback`, `/admin/compliance`) use the `firm-admin` policy and scope by
  `principal.FirmId`; the web wraps them in `RequireAdmin`.
- `/api/evals/reports` already returns every report summary, including the `intent` suite's metrics
  (`accuracy`, `accuracy:<lang>`, `accuracy:design|holdout`, `forcedWhenShould`, `unforcedWhenShouldNot`) and failures.
- The evals screen already draws a hand-built SVG line chart (`MetricTrend`); the telemetry screen draws CSS bars. The
  web has no chart library.

## Goals / Non-Goals

**Goals:**

- One request per screen load returns everything the charts need, aggregated on the server.
- Every aggregate is reproducible from the trace events by a pure function that tests can feed directly.
- The page states the floors it is judged against, taken from the running configuration, not hard-coded.

**Non-Goals:**

- Listing individual turns or linking a chart point to its trace. The trace endpoint only opens a turn for its owner,
  or for an admin when the turn is in the review queue, so most points could not be opened; a link that 404s is worse
  than none. The scatter carries numbers only.
- Statistics beyond trace retention. `Turns.Intent` outlives traces but holds only the final intent, none of Jev's
  numbers; a longer history would need its own aggregate store.
- Cross-firm or per-user breakdowns.
- Server-side caching. Revisit if the 7-day query becomes slow (see Risks).

## Decisions

### Aggregate on the server, from the trace events

The trace JSON of a turn also contains the question, the prompt and the model's messages. Parsing it in the browser
would ship all of that to the client; aggregating in the API returns numbers only and keeps the rule "no message
content" true by construction. The endpoint selects `(CreatedAt, Json)` for the principal's firm and window, the
aggregation parses each document with `JsonDocument`, takes the event whose `kind` is `intent`, and never reads any
other event. Alternative considered: SQLite `json_each` in raw SQL — faster, but a second query language for the same
logic and harder to unit test; rejected while volumes are a lab's.

### Jev events only

An event is Jev's when its `model` starts with `jev-` (both answered and failed Jev calls record it). The rest are
counted as `excludedEvents` so the screen can say that older turns were left out rather than silently shrinking.

### Three outcomes, derived from the reason

`used` = no reason; `gated` = `low confidence…`, `outside the domain…`, `answer is not one of the known intents`;
`failed` = anything else. The reason label in the breakdown is normalised so numbers in parentheses do not split
categories (`low confidence (0.43)` → `low confidence`; `outside the domain (0.03)` → `outside the domain`), except the
HTTP status of a rejection, which is the useful part (`rejected (503)` stays).

### Response shape

One DTO, `IntentStatsReport`, in `Maf.Lab.Domain/Intent` next to the other contracts:

- `window`, `from`, `to`, `bucketMinutes`; `settings` {`model`, `minConfidence`, `minInDomain`, `timeoutSeconds`}.
- `totals` {`classified`, `used`, `gated`, `failed`, `forced`, `excludedEvents`}.
- `pipeline` {`answered`, `belowConfidence`, `unknownChoice`, `forcingIntent`, `outsideDomain`, `forced`,
  `notForcingIntent`} — the counts along each edge of the diagram.
- `reasons` [{`outcome`, `label`, `count`}].
- `choices` [{`choice` (or `none`), `intent`, `count`}] — the choice-vs-proceeded matrix.
- `timeline` [{`start`, `used`, `gated`, `failed`, `timedOut`, `p50Ms`, `p90Ms`}] — every bucket present, empty ones
  with zeros and null latencies.
- `confidence`, `inDomain` [{`from`, `to`, `used`, `gated`}] — 20 bins of 0.05.
- `points` [{`confidence`, `inDomain`, `outcome`, `choice`}] — gated and used events, newest 1000.
- `meanProbabilities` [{`intent`, `mean`, `chosen`}] for the five intents.
- `latency` {`count`, `p50`, `p90`, `p99`, `max`, `bins` [{`fromMs`, `toMs` (null = overflow), `count`}]} — 100 ms bins
  up to the timeout, then one overflow bin.
- `models` [{`model`, `count`}].

Buckets: 5 minutes for `1h`, 1 hour for `24h`, 6 hours for `7d`. Percentiles are nearest-rank.

### Authorisation and scope

Under the `firm-admin` policy like the other firm-wide admin surfaces; the firm is `principal.FirmId`, and there is no
parameter to name another. Settings come from `IOptions<JevOptions>`.

### Charts are hand-built SVG

The page needs bars, stacked columns, histograms with a threshold line, a scatter, a heat-matrix and a line — each a
few dozen lines of SVG in React, like `MetricTrend`. A library (Recharts ≈ 100 kB gz with d3 transitive deps) would
be the first runtime dependency added for one screen and would need theming to the same tokens anyway. Rejected; no
package moves, recorded in DECISIONS.md.

Colour follows the dataviz method: outcomes are three categorical slots — used = blue, gated = orange, failed = aqua
(the first three slots validate for all pairs, which the scatter needs), with light and dark steps defined as CSS
tokens. Floors are dashed muted lines with a text label (a threshold, not a grid). Every multi-series chart has a
legend; every mark has a `<title>` tooltip; the choice matrix is a table with shaded cells, so its values are also
text. The five intents in the mean-probability chart are one series (one colour), not five.

### Eval history from the existing endpoint

The page calls `/api/evals/reports`, keeps `suite === "intent"`, and draws lines for `accuracy:en|bg|bg-latn`, one for
`accuracy:design|holdout`, and a small table of the newest run's metrics and failures (case id and reason — the reason
names the case's numbers, not its text). No new endpoint.

### Pipeline diagram

A static SVG flow — question → Jev (Choice + Noul, one call) → answered in time? → confidence ≥ floor? → procedural or
mixed? → in-domain ≥ floor? → forced — with the live floors in the boxes and the `pipeline` counts on the edges.

## Risks / Trade-offs

- [Parsing up to 7 days of trace JSON per request] → traces are capped at 1 MB each and a lab has hundreds of turns; the
  query reads only two columns through the `CreatedAt` index and filters by firm. The page refreshes every 60 s, not
  15 s. If it becomes slow: store the intent event's numbers in their own row at turn end.
- [Aggregates reveal volume and timing of a firm's usage] → firm-admin only, own firm only.
- [Reason strings are the contract between classifier and statistics] → the classification of reasons lives next to a
  unit test that enumerates every reason `JevIntentClassifier` can produce.
- [A new reason string appears] → it falls into `failed` under its own label, visible rather than lost.

## Migration Plan

Additive: a new endpoint and screen, no schema change. Rollback is removing them.
