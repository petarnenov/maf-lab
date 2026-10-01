## ADDED Requirements

### Requirement: When a run's work ended
A run SHALL record when its work ended: the first time it leaves the running states (`submitted`, `working`,
`verifying`), whether into `candidate` or into a final state. A later move of the run — accepting or discarding a
candidate — SHALL NOT change it, and a run that is still running SHALL have none.

A run stored before this was recorded SHALL get it once, at startup, from the time of its first recorded update in a
state other than a running one, or, when it has no such update, from its last change. A running run SHALL be left
without one.

#### Scenario: A candidate accepted later
- **WHEN** a run reaches `candidate` at 10:07 and is accepted at 11:30
- **THEN** its work ended at 10:07

#### Scenario: A run that fails
- **WHEN** a working run fails at the deadline
- **THEN** its work ended when it failed

#### Scenario: Still running
- **WHEN** a run is `working`
- **THEN** it has no end

#### Scenario: A run stored before ends were recorded
- **WHEN** the api starts with an accepted run that has no end and whose updates show it became `candidate` at 10:07
- **THEN** its work ended at 10:07, and a working run stored alongside it still has no end
