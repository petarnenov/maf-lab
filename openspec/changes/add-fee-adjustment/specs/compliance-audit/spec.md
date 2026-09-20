# Spec Delta

## MODIFIED Requirements

### Requirement: Actions that must be recorded
The record SHALL cover, at least: every tool invocation including attempts to call a tool that does not exist; the
deletion of a conversation; the production of a compliance export; every request received from another agent over
A2A; every request this system sends to another agent over A2A; and every step of a write — an adjustment
proposed, reviewed, confirmed, rejected or applied. Each record SHALL carry the kind of action, who performed it,
their firm, when, what it concerned in identifiers, and its outcome.

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

#### Scenario: A write is recorded at every step
- **WHEN** an adjustment is proposed, reviewed, confirmed and applied
- **THEN** a record exists for each, naming the person who acted, their firm, the account and the adjustment, and
  none of them carries the reason, the justification or the verdict's text

#### Scenario: A write that never happened is still recorded
- **WHEN** a proposal is rejected by the user, refused by the reviewer, or fails
- **THEN** the attempt is recorded with that outcome and nothing claims it was applied

### Requirement: The record can be browsed
An authorised person SHALL be able to read the record a page at a time, newest first, narrowed by any combination
of: the person who acted, the kind of action, and a period. Each page SHALL state whether more records follow and
how to ask for them. Every kind of action the system records SHALL be offered as a choice, so that no recorded kind
is invisible to the person browsing.

Reading SHALL be limited to the caller's own firm, taken from their token, exactly as the export is. Reading the
record MUST NOT alter it, and MUST NOT be recorded as an action — otherwise looking at the log would grow the log.

#### Scenario: Newest first, a page at a time
- **WHEN** a FIRM_ADMIN reads the record with a page size smaller than the number of records
- **THEN** the newest records are returned with a way to ask for the next page, and following it reaches the older ones without repeating any

#### Scenario: Narrowed by person and kind
- **WHEN** the record is read for one person and one kind of action
- **THEN** only that person's actions of that kind are returned

#### Scenario: Every recorded kind can be chosen
- **WHEN** the kinds offered for narrowing are inspected
- **THEN** every kind the system writes — including requests from and to other agents, and the steps of a write — is among them

#### Scenario: Another firm is not readable
- **WHEN** a FIRM_ADMIN reads the record while another firm has actions in the same period
- **THEN** none of them are returned, whatever parameters are given

#### Scenario: Reading leaves no trace
- **WHEN** the record is read
- **THEN** no new record is added and the chain head is unchanged

#### Scenario: Not an admin
- **WHEN** a user who is not a FIRM_ADMIN reads the record
- **THEN** the request is refused
