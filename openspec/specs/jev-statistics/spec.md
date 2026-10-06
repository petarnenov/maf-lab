# jev-statistics Specification

## Purpose
Aggregates what Jev did across every place a firm's chat turns call it — intent classification, prompt/content
screening, the passage-relevance gate and reranker, and data-turn tool routing — together with a cross-cutting view of
how many Jev requests each turn made, how long they took and how often Jev was unavailable, so Jev's behaviour and
availability can be read as a whole rather than one trace at a time.

## Requirements

### Requirement: Jev statistics endpoint
`GET /api/admin/jev-stats` SHALL return aggregates of the Jev call sites recorded in the turns' core records of trace
events — which exist without the monitor — of the caller's
firm's chat turns. The caller chooses the window from a fixed list (`1h`, `24h`, `7d`; default `24h`), and any other
window SHALL be refused as a validation problem.

Every number SHALL be computed on the server from the stored trace events only. The response SHALL contain no
question, answer, prompt, passage or snippet text, and no turn, conversation or user identifier. Only events whose
recorded model is a Jev model (`jev-*`) SHALL be counted.

Jev calls made outside a chat turn are recorded in no turn trace, so they SHALL NOT be counted. These are the
screening of an A2A partner's question and of the tool results on the A2A path.

The response SHALL contain the window and its bounds, and these sections:

- **Intent:** the same aggregate the intent-statistics endpoint returns, embedded unchanged. It holds outcomes,
  reasons, confidence and in-domain distributions, choice-vs-used, mean probabilities, latency and model versions.
- **Guardrail:** covers the prompt, tool-result and reviewer screenings:
  - how many of each were screened, and their decisions (pass, blocked, withheld, unscreened);
  - the question that tripped a block or a withholding, counted;
  - the counts of blocked, withheld and unscreened;
  - the latency of the screenings that make their own Jev request;
  - screenings over time, with the unscreened (Jev-unavailable) ones marked.
- **Relevance:** covers the passage-relevance gate and the Jev reranker:
  - how many searches Jev judged;
  - how many were silenced by the gate;
  - how many were judged with the Jev reranker;
  - how many were left ungated because Jev was unavailable;
  - the distribution of each search's top relevance against the floor, split by whether the search was silenced;
  - the judge latency;
  - searches over time, with the silenced and unavailable ones marked.
- **Routing:** covers data-turn tool routing:
  - how many data turns there were;
  - how many were routed, and to which tool;
  - the reasons a data turn was not routed;
  - the median model calls a turn made, routed against unrouted;
  - the routed turns' latency.
- **Overview:** cross-cutting across every site:
  - the total Jev requests and how many were unavailable;
  - the requests per classified turn;
  - a per-site breakdown (requests, unavailable, latency percentiles) for the sites that make their own Jev request;
  - total requests and unavailability over time, in fixed buckets.

  A prompt or routing question that rides inside the intent request SHALL NOT be counted as a separate request.

The counts SHALL come from these records:
- **Judged searches:** from the turn's `relevance` events. A turn recorded before those events existed SHALL be read
  from the relevance judgment in its retrieval diagnostics instead, and no search SHALL be counted twice.
- **Tool-result screening requests:** from the request count the screening recorded. A screening recorded without one
  SHALL be counted as one request per screened item.

#### Scenario: Aggregates across the call sites
- **WHEN** a TENANT_ADMIN requests the statistics after turns that classified an intent, screened prompts and tool
  results, gated and reranked searches, and routed data turns
- **THEN** each site's section reports its counts, and the overview's total requests equal the intent classifications
  plus the content screening requests plus the judged searches, with the unavailable ones counted apart

#### Scenario: Jev unavailability is visible across every site
- **WHEN** the window contains a timed-out intent classification, a tool result whose screening was unscreened, and a
  search left ungated because the judge was unavailable
- **THEN** each is counted as unavailable in its own section, and all three are summed into the overview's
  unavailability over time

#### Scenario: Judged searches counted without retrieval diagnostics
- **WHEN** turns ran with retrieval diagnostics turned off and their searches were judged by Jev
- **THEN** the relevance section and the overview count those searches

#### Scenario: Older traces counted once
- **WHEN** the window holds turns recorded before `relevance` events existed and turns recorded after, some of them
  with retrieval diagnostics
- **THEN** every judged search is counted exactly once

#### Scenario: A2A calls are not counted
- **WHEN** an A2A partner's question and its tool results were screened in the window
- **THEN** none of those screenings appears in any section or in the overview

#### Scenario: Only Jev events count
- **WHEN** the window contains intent events written by an earlier classifier
- **THEN** they are left out of every aggregate and reported only as excluded, as the intent section already does

#### Scenario: Unknown window
- **WHEN** the caller asks for window `30d`
- **THEN** the response is a validation problem naming the allowed windows

#### Scenario: Nothing recorded
- **WHEN** no Jev event falls in the window
- **THEN** the response has zero totals and empty distributions in every section, rather than an error

### Requirement: Jev statistics are firm-scoped and admin-only
The statistics MUST be computed only from turns of the firm in the caller's token; the endpoint MUST NOT accept a firm
or tenant parameter. Callers without the TENANT_ADMIN role MUST be refused.

#### Scenario: Other firm's turns are invisible
- **WHEN** a TENANT_ADMIN of tenant A requests the statistics and tenant B has recorded turns in the window
- **THEN** none of tenant B's turns is counted in any section

#### Scenario: Not an admin
- **WHEN** a USER requests the statistics
- **THEN** the response is forbidden

### Requirement: No message content in Jev statistics
The response MUST NOT contain any question, answer, prompt, passage or snippet text, nor turn, conversation or user
identifiers. Computing it MUST NOT write message content to logs.

#### Scenario: Response carries numbers only
- **WHEN** the statistics are computed over turns with distinctive question text
- **THEN** that text appears neither in the response body nor in any log line

### Requirement: Jev statistics per domain
The Jev statistics SHALL count judged searches per domain, taking the domain of the call that searched.

The statistics SHALL gain a Domains section, counting:
- the turns with a domain verdict;
- the turns per verdict: billing, portfolio, both or none;
- the turns whose calls crossed from one domain to the other;
- of the turns that called a tool, how many touched exactly the domains predicted.

The Domains section SHALL hold numbers only.

#### Scenario: A crossing turn
- **WHEN** a turn predicted both domains, called a billing search and a portfolio search, and the portfolio search was silenced
- **THEN** it counts once as "both", once as crossed and agreed, and the silenced search counts under portfolio

### Requirement: The answer check in the Jev statistics
The Jev statistics SHALL count the answer check as a request-bearing site named `answer`: its requests, how many were
unavailable (a request that ended `unchecked`) and its latency percentiles SHALL appear in the overview's per-site
breakdown, and its requests SHALL be included in the overview's totals and in its requests and unavailability over
time. An answer check that sent no request (disabled, no key, sources over cap) SHALL NOT be counted as a request.

The response SHALL also carry an answer-check section: how many turns were checked (received a verdict other than
`unchecked`), how many of those were below the relevance floor and how many below the grounding floor — each against
the floor recorded with its event — how many were `uncertain` (optional in the contract, so an older client still
reads the response), how many turns were left unchecked and how many of those because Jev was unavailable, and the
check's latency percentiles and histogram. Numbers only.

#### Scenario: The answer site in the overview
- **WHEN** the window holds two checked answers and one whose check was rejected by Jev
- **THEN** the overview's `answer` site reports three requests and one unavailable, and the overview's total includes them

#### Scenario: The answer-check section
- **WHEN** the window holds one passing answer, one below the grounding floor and one unchecked
- **THEN** the section reports two checked, one not grounded, none not relevant and one unchecked

#### Scenario: Uncertain answers are counted apart
- **WHEN** the window holds one passing answer and one `uncertain` answer
- **THEN** the section reports two checked, one uncertain, and none below either floor

#### Scenario: Over the cap sends no request
- **WHEN** the window holds one answer left `unchecked` because its sources were over the cap
- **THEN** the `answer` site counts no request for it and no unavailability, and the section counts it as unchecked

### Requirement: Calls skipped by an open circuit are counted apart
A Jev call that an open circuit skipped sent nothing to Jev. Such a call is recorded with reason `circuit open` and with
no request. The statistics SHALL count it as *skipped*, per site. It SHALL NOT be counted as a request, and SHALL NOT be
counted among the unavailable requests. This applies to each request-bearing site: intent, guardrail content screening,
relevance and answer.

The overview SHALL gain:
- each site's skipped count;
- the total skipped count;
- the skipped count in each bucket of the timeline.

Every existing field SHALL keep its meaning. In particular:
- the overview's total requests SHALL remain the requests actually sent;
- the requests per classified turn SHALL count only requests that were sent.

In the intent section, a classification skipped by an open circuit SHALL remain a failed classification, with its own
reason `circuit open`. In the other sections, a skipped call SHALL remain counted wherever those sections count
unscreened screenings, ungated searches and unchecked answers. The skipped count SHALL only take it out of the requests.

Latency percentiles SHALL NOT include skipped calls.

#### Scenario: Skipped calls in the overview
- **WHEN** the window holds 10 intent classifications, of which 2 timed out and 3 were skipped by an open circuit
- **THEN** the intent site reports 7 requests, 2 unavailable and 3 skipped, and the overview's totals and timeline carry
  the same numbers in the buckets where they occurred

#### Scenario: Skipped search stays ungated
- **WHEN** a search was left ungated because the circuit was open
- **THEN** the relevance section counts it as ungated for unavailability, and the overview counts it as skipped at the
  relevance site, not as a request

#### Scenario: Skipped answer check
- **WHEN** an answer check returned `unchecked` with reason `circuit open`
- **THEN** the answer-check section counts it as unchecked, and the `answer` site counts one skipped call and no
  request

#### Scenario: Latency without skipped calls
- **WHEN** a site's calls in the window are all skipped
- **THEN** that site reports no latency percentiles rather than zero
