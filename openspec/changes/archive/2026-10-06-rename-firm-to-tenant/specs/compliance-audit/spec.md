# Spec Delta

## MODIFIED Requirements

### Requirement: Verification
An authorised person SHALL be able to verify the chain for their own tenant and be told, in one answer: whether it is
intact, how many records were checked, the range they cover, the digest of the most recent record, and — when it is
not intact — the first record at which it breaks.

#### Scenario: Intact chain
- **WHEN** a TENANT_ADMIN verifies the audit chain and nothing has been tampered with
- **THEN** the answer says it is intact and gives the number of records, the range and the head digest

#### Scenario: Broken chain
- **WHEN** a record has been altered
- **THEN** the answer says it is broken and identifies the first record that does not match

#### Scenario: Not an admin
- **WHEN** a user who is not a TENANT_ADMIN asks to verify
- **THEN** the request is refused

### Requirement: Actions that must be recorded
The record SHALL cover, at least: every tool invocation including attempts to call a tool that does not exist; the
deletion of a conversation; the production of a compliance export; every request received from another agent over
A2A; every request this system sends to another agent over A2A; and every step of a write — an adjustment
proposed, reviewed, confirmed, rejected or applied. Each record SHALL carry the kind of action, who performed it,
their tenant, when, what it concerned in identifiers, and its outcome.

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
- **THEN** a record exists for each, naming the person who acted, their tenant, the account and the adjustment, and
  none of them carries the reason, the justification or the verdict's text

#### Scenario: A write that never happened is still recorded
- **WHEN** a proposal is rejected by the user, refused by the reviewer, or fails
- **THEN** the attempt is recorded with that outcome and nothing claims it was applied

### Requirement: Compliance export
A TENANT_ADMIN SHALL be able to obtain, for a period they specify, a package containing the audited actions, the
turns and the conversations of **their own tenant**. The tenant SHALL be taken from the requester's token and never from
a parameter, so no request can reach another tenant's data. Naming a user SHALL narrow the package to that person, for
answering a data subject request.

The package SHALL carry a manifest stating who produced it, when, the period, the counts of what it contains, a
digest over its content, and the audit chain head at the time — so that its integrity can be checked later by
someone who did not produce it.

#### Scenario: Firm-scoped export
- **WHEN** a TENANT_ADMIN exports a period
- **THEN** the package contains their tenant's audited actions, turns and conversations for that period, and nothing of any other firm

#### Scenario: Subject-scoped export
- **WHEN** a TENANT_ADMIN exports a period naming one user of their tenant
- **THEN** the package is limited to that user's conversations, turns and actions

#### Scenario: Another firm cannot be reached
- **WHEN** a TENANT_ADMIN attempts to export another tenant by any parameter
- **THEN** the package still contains only their own tenant's data

#### Scenario: The manifest allows a later check
- **WHEN** the package is examined afterwards
- **THEN** its manifest gives the producer, the time, the period, the counts, a digest of the content and the chain head

#### Scenario: Deleted conversations are included
- **WHEN** the period contains a conversation the user deleted
- **THEN** it appears in the package, marked as deleted with the time it was deleted

### Requirement: The record can be browsed
An authorised person SHALL be able to read the record a page at a time, newest first, narrowed by any combination
of: the person who acted, the kind of action, and a period. Each page SHALL state whether more records follow and
how to ask for them. Every kind of action the system records SHALL be offered as a choice, so that no recorded kind
is invisible to the person browsing.

Reading SHALL be limited to the caller's own tenant, taken from their token, exactly as the export is. Reading the
record MUST NOT alter it, and MUST NOT be recorded as an action — otherwise looking at the log would grow the log.

#### Scenario: Newest first, a page at a time
- **WHEN** a TENANT_ADMIN reads the record with a page size smaller than the number of records
- **THEN** the newest records are returned with a way to ask for the next page, and following it reaches the older ones without repeating any

#### Scenario: Narrowed by person and kind
- **WHEN** the record is read for one person and one kind of action
- **THEN** only that person's actions of that kind are returned

#### Scenario: Every recorded kind can be chosen
- **WHEN** the kinds offered for narrowing are inspected
- **THEN** every kind the system writes — including requests from and to other agents, and the steps of a write — is among them

#### Scenario: Another firm is not readable
- **WHEN** a TENANT_ADMIN reads the record while another tenant has actions in the same period
- **THEN** none of them are returned, whatever parameters are given

#### Scenario: Reading leaves no trace
- **WHEN** the record is read
- **THEN** no new record is added and the chain head is unchanged

#### Scenario: Not an admin
- **WHEN** a user who is not a TENANT_ADMIN reads the record
- **THEN** the request is refused
