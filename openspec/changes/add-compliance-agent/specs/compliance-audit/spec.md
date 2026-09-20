# Spec Delta

## MODIFIED Requirements

### Requirement: Actions that must be recorded
The record SHALL cover, at least: every tool invocation including attempts to call a tool that does not exist; the
deletion of a conversation; the production of a compliance export; every request received from another agent over
A2A; and every request this system sends to another agent over A2A. Each record SHALL carry the kind of action,
who performed it, their firm, when, what it concerned in identifiers, and its outcome.

Recording an action MUST NOT depend on the action's success: a refused or failed action SHALL be recorded with that
outcome.

#### Scenario: Deletion is recorded
- **WHEN** a user deletes a conversation
- **THEN** a record exists naming that user, that conversation and the time of deletion

#### Scenario: Export is recorded
- **WHEN** a compliance export is produced
- **THEN** a record exists naming who produced it, the range it covered and how many records it contained

#### Scenario: A refused action
- **WHEN** a user attempts to delete a conversation that is not theirs
- **THEN** nothing is deleted and the attempt is not attributed to them as a deletion

#### Scenario: A partner system's request is recorded
- **WHEN** a partner system sends a request over A2A
- **THEN** a record exists of that kind, naming the partner, the operation and the task it concerns

#### Scenario: A consultation of another agent is recorded
- **WHEN** this system consults a sub-agent over A2A
- **THEN** a record exists of that kind, naming the agent consulted, the task it concerns, the outcome and the
  duration — and none of the message content
