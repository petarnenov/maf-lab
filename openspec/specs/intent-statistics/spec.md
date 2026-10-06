# intent-statistics Specification

## Purpose
Aggregates what the intent classifier (Jev) answered on a firm's chat turns — outcomes, confidences, domain
probabilities, latency, model versions — so its behaviour can be read as a whole rather than one trace at a time.

## Requirements

### Requirement: Intent statistics endpoint
`GET /api/admin/intent-stats` SHALL return aggregates of the `intent` events of the caller's tenant's turns — read from
each turn's core record of trace events, which exists without the monitor —
recorded within a window the caller chooses from a fixed list (`1h`, `24h`, `7d`; default `24h`). Any other window
SHALL be refused as a validation problem. Only events whose recorded model is a Jev model SHALL be counted; events
written by earlier classifiers SHALL be reported only as a count of excluded events.

Each counted event SHALL fall into exactly one outcome:
- **used** — Jev answered and its choice was acted on (no reason recorded);
- **gated** — Jev answered but the answer was not acted on: low confidence, outside the domain, or an unknown choice;
- **failed** — no usable answer: timed out, rejected with a status, no answer, no key, classification disabled, or an
  error.

The response SHALL contain: the window and its bounds; the configured model, confidence floor, in-domain floor and
timeout; totals by outcome and the number of turns with forced retrieval; counts per reason; counts of Jev's choice
against the intent the turn proceeded with; counts over time in fixed buckets by outcome, with the number of timeouts
and the median and p90 latency per bucket; confidence and in-domain probability histograms by outcome; confidence and
in-domain pairs for gated and used events (numbers and outcome only); the mean probability Jev gave each intent;
latency p50, p90, p99 and maximum and a latency histogram; and counts per model version.

This same aggregate SHALL also be available embedded, unchanged, as the intent section of the Jev statistics endpoint
(`GET /api/admin/jev-stats`), computed by the same aggregation over the same trace events.

#### Scenario: Aggregates over the window
- **WHEN** a TENANT_ADMIN requests the statistics after turns that were used, gated below the confidence floor, gated
  outside the domain and timed out
- **THEN** each turn is counted once under its outcome and reason, and the forced count equals the used procedural and
  mixed turns

#### Scenario: Earlier classifiers are not Jev
- **WHEN** the window contains intent events recorded by an earlier classifier
- **THEN** they are left out of every aggregate and reported only as excluded

#### Scenario: Unknown window
- **WHEN** the caller asks for window `30d`
- **THEN** the response is a validation problem naming the allowed windows

#### Scenario: Nothing recorded
- **WHEN** no Jev event falls in the window
- **THEN** the response has zero totals and empty distributions rather than an error

#### Scenario: Embedded in the Jev overview
- **WHEN** a TENANT_ADMIN requests `GET /api/admin/jev-stats` for a window
- **THEN** its intent section is the identical aggregate the intent-stats endpoint returns for that firm and window

### Requirement: Intent statistics are firm-scoped and admin-only
The statistics MUST be computed only from turns of the firm in the caller's token; the endpoint MUST NOT accept a firm
or tenant parameter. Callers without the TENANT_ADMIN role MUST be refused.

#### Scenario: Other firm's turns are invisible
- **WHEN** a TENANT_ADMIN of tenant A requests the statistics and tenant B has recorded turns in the window
- **THEN** none of tenant B's turns is counted

#### Scenario: Not an admin
- **WHEN** a USER requests the statistics
- **THEN** the response is forbidden

### Requirement: No message content in intent statistics
The response MUST NOT contain any question, answer, prompt or snippet text, nor turn, conversation or user
identifiers. Computing it MUST NOT write message content to logs.

#### Scenario: Response carries numbers only
- **WHEN** the statistics are computed over turns with distinctive question text
- **THEN** that text appears neither in the response body nor in any log line
