# Spec Delta

## ADDED Requirements

### Requirement: Targets stop cleanly on Ctrl+C
Every make target and the script behind it SHALL stop on Ctrl+C or SIGTERM as stop-anything says: the running tool
stops at a safe point, server work the target started (an admin job, a test-generation run, an index job started by
verification) is cancelled before it exits, temporary files and containers it started for itself are removed, and the
target exits 130 with one line saying it was cancelled and what to run again.

#### Scenario: Ctrl+C during make coverage
- **WHEN** a developer presses Ctrl+C while `make coverage` waits on the refresh it started
- **THEN** the refresh job is cancelled, the target says so and exits 130

#### Scenario: Ctrl+C during make verify
- **WHEN** a developer presses Ctrl+C while verification has an api replica stopped
- **THEN** the replica is started again, the index job verification started is cancelled, and the target exits 130
