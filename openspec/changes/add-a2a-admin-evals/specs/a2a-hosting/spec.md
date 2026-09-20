# Spec Delta

## MODIFIED Requirements

### Requirement: Two shapes of work
A request the assistant can answer at once SHALL be answered with a message. A request that takes time SHALL be
answered with a task that the caller can follow: it SHALL report progress while it works, and SHALL end with a
structured artifact describing the result.

A task that cannot proceed without something the caller did not supply SHALL enter the state that asks for input,
SHALL say what is missing, and SHALL continue when the caller sends it under the same task. A task SHALL be
cancellable while it is working, and cancelling SHALL stop it. A task SHALL be cancellable both by the partner
that started it and by a firm admin of the firm it concerns, since the work is done on that firm's data.

#### Scenario: A question is answered directly
- **WHEN** a partner asks for the status of a known run
- **THEN** the answer is a message, not a task

#### Scenario: Work that takes time
- **WHEN** a partner asks to start a billing run
- **THEN** a task is created, its progress is reported while it works, and it completes with an artifact describing the final status

#### Scenario: Something is missing
- **WHEN** a partner asks to start a run without saying which period
- **THEN** the task asks for the period and says so, and supplying it under the same task lets the task finish

#### Scenario: Cancelled while working
- **WHEN** a partner cancels a working task
- **THEN** the task stops and reports that it was cancelled

#### Scenario: Cancelled by the firm it concerns
- **WHEN** a firm admin cancels a working task started by a partner against their firm
- **THEN** the task stops and reports that it was cancelled
