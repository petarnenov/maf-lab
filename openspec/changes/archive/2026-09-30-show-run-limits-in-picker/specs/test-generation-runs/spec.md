# Spec Delta

## ADDED Requirements

### Requirement: Per-run limits
A start request MAY carry limits: max attempts, tool rounds per attempt and test runs per attempt. A limit that is
absent SHALL take its default (10, 40 and 2). The api SHALL reject a limit outside its bounds (attempts 1–10, tool
rounds 1–40, test runs 0–2) as invalid, before any run is created and without changing the threshold. The api SHALL
store the limits on the run, SHALL pass exactly them to the task, and SHALL return them in the run's summary. A run
stored before limits existed SHALL read as having the defaults.

#### Scenario: Start with limits
- **WHEN** an administrator starts a run with max attempts 4 and 20 tool rounds per attempt
- **THEN** the task carries max attempts 4, 20 tool rounds and 2 test runs per attempt, and the run's summary shows them

#### Scenario: Start without limits
- **WHEN** a start request carries no limits
- **THEN** the task carries max attempts 10, 40 tool rounds and 2 test runs per attempt

#### Scenario: Limit out of bounds
- **WHEN** a start request asks for 11 attempts or 0 tool rounds
- **THEN** it is rejected as invalid, no run is created and the threshold is unchanged
