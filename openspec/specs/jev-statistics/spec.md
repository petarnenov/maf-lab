# jev-statistics Specification

## Purpose
Aggregates what Jev did across every place a firm's chat turns call it — intent classification, prompt/content
screening, the passage-relevance gate and reranker, and data-turn tool routing — together with a cross-cutting view of
how many Jev requests each turn made, how long they took and how often Jev was unavailable, so Jev's behaviour and
availability can be read as a whole rather than one trace at a time.

## Requirements

### Requirement: Jev statistics endpoint
`GET /api/admin/jev-stats` SHALL return aggregates of the Jev call sites recorded in the caller's firm's turn traces
within a window the caller chooses from a fixed list (`1h`, `24h`, `7d`; default `24h`). Any other window SHALL be
refused as a validation problem. Every number SHALL be computed on the server from the stored trace events only; the
response SHALL contain no question, answer, prompt, passage or snippet text, and no turn, conversation or user
identifier. Only events whose recorded model is a Jev model (`jev-*`) SHALL be counted.

The response SHALL contain the window and its bounds, and these sections:

- **Intent** — the same aggregate the intent-statistics endpoint returns (outcomes, reasons, confidence and in-domain
  distributions, choice-vs-used, mean probabilities, latency and model versions), embedded unchanged.
- **Guardrail** — for the prompt, tool-result and reviewer/partner screenings: how many of each were screened and
  their decisions (pass, blocked, withheld, unscreened); the question that tripped a block or a withholding, counted;
  the counts of blocked, withheld and unscreened; the latency of the screenings that make their own Jev request; and
  screenings over time with the unscreened (Jev-unavailable) ones marked.
- **Relevance** — for the passage-relevance gate and Jev reranker: how many searches Jev judged, how many were
  silenced by the gate, how many judged with the Jev reranker, how many were left ungated because Jev was unavailable;
  the distribution of each search's top relevance against the floor, split by whether the search was silenced; the
  judge latency; and searches over time with the silenced and unavailable ones marked.
- **Routing** — for data-turn tool routing: how many data turns there were, how many were routed and to which tool,
  the reasons a data turn was not routed, the median model calls a turn made routed against unrouted, and the routed
  turns' latency.
- **Overview** — cross-cutting across every site: the total Jev requests and how many were unavailable, the requests
  per classified turn, a per-site breakdown (requests, unavailable, latency percentiles) for the sites that make their
  own Jev request, and total requests and unavailability over time in fixed buckets. A prompt or routing question that
  rides inside the intent request SHALL NOT be counted as a separate request.

#### Scenario: Aggregates across the call sites
- **WHEN** a FIRM_ADMIN requests the statistics after turns that classified an intent, screened prompts and tool
  results, gated and reranked searches, and routed data turns
- **THEN** each site's section reports its counts, and the overview's total requests equal the intent classifications
  plus the content screenings plus the judged searches, with the unavailable ones counted apart

#### Scenario: Jev unavailability is visible across every site
- **WHEN** the window contains a timed-out intent classification, a tool result whose screening was unscreened, and a
  search left ungated because the judge was unavailable
- **THEN** each is counted as unavailable in its own section and all three are summed into the overview's
  unavailability over time

#### Scenario: Only Jev events count
- **WHEN** the window contains intent events written by an earlier classifier
- **THEN** they are left out of every aggregate and reported only as excluded, as the intent section already does

#### Scenario: Unknown window
- **WHEN** the caller asks for window `30d`
- **THEN** the response is a validation problem naming the allowed windows

#### Scenario: Nothing recorded
- **WHEN** no Jev event falls in the window
- **THEN** the response has zero totals and empty distributions in every section rather than an error

### Requirement: Jev statistics are firm-scoped and admin-only
The statistics MUST be computed only from turns of the firm in the caller's token; the endpoint MUST NOT accept a firm
or tenant parameter. Callers without the FIRM_ADMIN role MUST be refused.

#### Scenario: Other firm's turns are invisible
- **WHEN** a FIRM_ADMIN of firm A requests the statistics and firm B has recorded turns in the window
- **THEN** none of firm B's turns is counted in any section

#### Scenario: Not an admin
- **WHEN** an ADVISOR requests the statistics
- **THEN** the response is forbidden

### Requirement: No message content in Jev statistics
The response MUST NOT contain any question, answer, prompt, passage or snippet text, nor turn, conversation or user
identifiers. Computing it MUST NOT write message content to logs.

#### Scenario: Response carries numbers only
- **WHEN** the statistics are computed over turns with distinctive question text
- **THEN** that text appears neither in the response body nor in any log line
