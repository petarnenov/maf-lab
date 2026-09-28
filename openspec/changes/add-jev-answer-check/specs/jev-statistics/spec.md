# Spec Delta

## ADDED Requirements

### Requirement: The answer check in the Jev statistics
The Jev statistics SHALL count the answer check as a request-bearing site named `answer`: its requests, how many were
unavailable (a request that ended `unchecked`) and its latency percentiles SHALL appear in the overview's per-site
breakdown, and its requests SHALL be included in the overview's totals and in its requests and unavailability over
time. An answer check that sent no request (disabled, no key) SHALL NOT be counted as a request.

The response SHALL also carry an answer-check section: how many turns were checked (received a verdict other than
`unchecked`), how many of those were below the relevance floor and how many below the grounding floor — each against
the floor recorded with its event — how many turns were left unchecked and how many of those because Jev was
unavailable, and the check's latency percentiles and histogram. Numbers only.

#### Scenario: The answer site in the overview
- **WHEN** the window holds two checked answers and one whose check was rejected by Jev
- **THEN** the overview's `answer` site reports three requests and one unavailable, and the overview's total includes them

#### Scenario: The answer-check section
- **WHEN** the window holds one passing answer, one below the grounding floor and one unchecked
- **THEN** the section reports two checked, one not grounded, none not relevant and one unchecked
