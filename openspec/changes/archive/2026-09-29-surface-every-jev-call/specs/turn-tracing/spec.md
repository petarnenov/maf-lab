# Spec Delta

## ADDED Requirements

### Requirement: Jev relevance judgments are traced
Every search in a turn that asked Jev for a relevance judgment SHALL add a `relevance` event to the turn's trace,
after that search's tool result. It SHALL be recorded whether or not retrieval diagnostics were requested.

The event SHALL carry:
- the tool call it concerns;
- whether the gate was on;
- the floor;
- how many candidates were judged;
- the highest probability;
- whether the gate silenced the search;
- whether Jev's answer ordered the results;
- the versioned model;
- how long the judgment took;
- when Jev did not answer, why.

Its duration SHALL be the judgment's duration. Its title SHALL name Jev and say the outcome in one of three ways:
- **silenced:** the highest probability is below the floor;
- **kept:** the highest probability is at or above the floor, or the gate is off;
- **ungated:** Jev was unavailable, with the reason.

The event MUST NOT contain the query, a passage, a chunk id, or any credential.

A search that did not ask Jev SHALL add no `relevance` event.

#### Scenario: A search the gate kept
- **WHEN** a turn's search is judged with a highest probability of 0.87 against a floor of 0.5
- **THEN** the trace holds a `relevance` event for that call whose title names Jev, 0.87, the floor and "kept", and
  whose duration is the judge's latency

#### Scenario: A search the gate silenced
- **WHEN** every judged candidate's probability is below the floor
- **THEN** the `relevance` event says the search was silenced, with the highest probability and the floor

#### Scenario: Jev unavailable
- **WHEN** the relevance request times out
- **THEN** the `relevance` event says the search was left ungated and gives the reason

#### Scenario: Diagnostics not requested
- **WHEN** retrieval diagnostics are turned off and a search asks Jev for a judgment
- **THEN** the trace still holds the `relevance` event for that search, and holds no `retrieval` event

#### Scenario: No judgment asked
- **WHEN** a search runs with the relevance gate off and a reranker other than Jev's
- **THEN** the trace holds no `relevance` event for it

### Requirement: A screening's Jev requests are counted
A `guardrail` event for a tool result SHALL record:
- how many Jev requests the screening made (one per non-empty item);
- as its duration, the time from the first request to the last answer.

The latency of each item SHALL stay in the event. When the screening made more than one request, the title SHALL say
how many.

#### Scenario: A search result with five excerpts
- **WHEN** a search result with five non-empty excerpts is screened
- **THEN** the `guardrail` event records five requests, each excerpt's own latency, and a duration covering the whole
  screening, and its title mentions the five requests

#### Scenario: A single screened result
- **WHEN** a tool result other than a document search is screened
- **THEN** the `guardrail` event records one request, and its title does not mention a request count
