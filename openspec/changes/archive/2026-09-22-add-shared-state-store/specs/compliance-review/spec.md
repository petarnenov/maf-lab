# Spec Delta

## ADDED Requirements

### Requirement: The reviewer's work survives the replica that took it
The reviewer's tasks and webhook registrations SHALL be kept outside the serving process, so that any replica can
report a task's state and deliver the changes a caller registered for. A task started through one replica SHALL be
readable through another, and a webhook registered through one SHALL be honoured whichever replica sees the state
change.

#### Scenario: A verdict asked for through another replica
- **WHEN** a review is started through one reviewer replica and its task is fetched through another
- **THEN** the second replica reports the same task with the same state

#### Scenario: A webhook registered on one replica
- **WHEN** a webhook is registered through one reviewer replica and the task changes state on another
- **THEN** the delivery is made
