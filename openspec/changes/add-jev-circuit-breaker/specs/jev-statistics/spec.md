# Spec Delta

## ADDED Requirements

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
