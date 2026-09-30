# Spec Delta

## MODIFIED Requirements

### Requirement: Per-run limits
A start request MAY carry limits: max attempts, tool rounds per attempt, test runs per attempt, the run deadline in
minutes and the suspected bugs the run may report. A limit that is absent SHALL take its default (10, 40, 2, the
configured deadline and 3). The api SHALL reject a limit outside its bounds (attempts 1–10, tool rounds 1–40, test runs
0–2, deadline from 10 minutes to the configured deadline, suspected bugs 0–3) as invalid, before any run is created and
without changing the threshold. The api SHALL store the limits on the run, SHALL pass the attempt, round, test-run and
suspected-bug limits to the task, and SHALL return all of them in the run's summary (a run without its own deadline shows none, meaning the configured one). The api SHALL cancel a run when
its own deadline passes, and SHALL verify a candidate against the run's own suspected-bug limit. A run stored before
limits existed SHALL read as having the defaults.

#### Scenario: Start with limits
- **WHEN** an administrator starts a run with max attempts 4 and 20 tool rounds per attempt
- **THEN** the task carries max attempts 4, 20 tool rounds and 2 test runs per attempt, and the run's summary shows them

#### Scenario: Start without limits
- **WHEN** a start request carries no limits
- **THEN** the task carries max attempts 10, 40 tool rounds, 2 test runs per attempt and 3 suspected bugs, and the run's deadline is the configured one

#### Scenario: Limit out of bounds
- **WHEN** a start request asks for 11 attempts, 0 tool rounds, a 5-minute deadline or 4 suspected bugs
- **THEN** it is rejected as invalid, no run is created and the threshold is unchanged

#### Scenario: The run's own deadline
- **WHEN** a run started with a 30-minute deadline is still working 31 minutes after it started
- **THEN** it is canceled and fails with reason `deadline`

#### Scenario: Verified with the run's bug limit
- **WHEN** a run started with a suspected-bug limit of 0 returns a candidate that reports a suspected bug
- **THEN** its verification fails on a guardrail, no proof run starts, and no issue is opened
