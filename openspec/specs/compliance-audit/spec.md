# compliance-audit Specification

## Purpose
Keeps a tamper-evident record of who did what in the system, and turns that record into something an authorised
person can be handed and can check, without ever widening what one firm may see of another.

## Requirements

### Requirement: Every audited action is chained
Each audited action SHALL be recorded with a digest computed over its own content together with the digest of the
action recorded before it, so that the records form a chain. Changing a recorded action, removing one, or inserting
one SHALL break the chain.

The recorded content SHALL remain free of message content: the identifiers of an action, never its text. The chain
SHALL be computed over exactly the fields that are stored, so that verification needs nothing but the records
themselves.

Records written before chaining began SHALL NOT be rewritten; verification SHALL report from where the chain is
established.

#### Scenario: A chain is formed
- **WHEN** three actions are recorded in turn
- **THEN** each record after the first carries the previous record's digest, and its own digest covers that link

#### Scenario: An altered record is detectable
- **WHEN** a stored record's content is changed directly in the store
- **THEN** verification fails and names that record as the first broken link

#### Scenario: A removed record is detectable
- **WHEN** a stored record is deleted directly from the store
- **THEN** verification fails at the record that followed it

#### Scenario: Records predating the chain
- **WHEN** the store already held records from before chaining began
- **THEN** verification passes for the chained part and reports where it begins

### Requirement: Verification
An authorised person SHALL be able to verify the chain for their own firm and be told, in one answer: whether it is
intact, how many records were checked, the range they cover, the digest of the most recent record, and — when it is
not intact — the first record at which it breaks.

#### Scenario: Intact chain
- **WHEN** a FIRM_ADMIN verifies the audit chain and nothing has been tampered with
- **THEN** the answer says it is intact and gives the number of records, the range and the head digest

#### Scenario: Broken chain
- **WHEN** a record has been altered
- **THEN** the answer says it is broken and identifies the first record that does not match

#### Scenario: Not an admin
- **WHEN** a user who is not a FIRM_ADMIN asks to verify
- **THEN** the request is refused

### Requirement: Actions that must be recorded
The record SHALL cover, at least: every tool invocation including attempts to call a tool that does not exist; the
deletion of a conversation; and the production of a compliance export. Each record SHALL carry the kind of action,
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

### Requirement: Compliance export
A FIRM_ADMIN SHALL be able to obtain, for a period they specify, a package containing the audited actions, the
turns and the conversations of **their own firm**. The firm SHALL be taken from the requester's token and never from
a parameter, so no request can reach another firm's data. Naming a user SHALL narrow the package to that person, for
answering a data subject request.

The package SHALL carry a manifest stating who produced it, when, the period, the counts of what it contains, a
digest over its content, and the audit chain head at the time — so that its integrity can be checked later by
someone who did not produce it.

#### Scenario: Firm-scoped export
- **WHEN** a FIRM_ADMIN exports a period
- **THEN** the package contains their firm's audited actions, turns and conversations for that period, and nothing of any other firm

#### Scenario: Subject-scoped export
- **WHEN** a FIRM_ADMIN exports a period naming one user of their firm
- **THEN** the package is limited to that user's conversations, turns and actions

#### Scenario: Another firm cannot be reached
- **WHEN** a FIRM_ADMIN attempts to export another firm by any parameter
- **THEN** the package still contains only their own firm's data

#### Scenario: The manifest allows a later check
- **WHEN** the package is examined afterwards
- **THEN** its manifest gives the producer, the time, the period, the counts, a digest of the content and the chain head

#### Scenario: Deleted conversations are included
- **WHEN** the period contains a conversation the user deleted
- **THEN** it appears in the package, marked as deleted with the time it was deleted

### Requirement: The record is queryable by person
The record SHALL be efficiently queryable by the person who acted as well as by firm and time, since an
investigation starts from a person.

#### Scenario: What one person did
- **WHEN** the actions of one user over a period are requested
- **THEN** they are returned without scanning unrelated records

### Requirement: The record can be browsed
An authorised person SHALL be able to read the record a page at a time, newest first, narrowed by any combination
of: the person who acted, the kind of action, and a period. Each page SHALL state whether more records follow and
how to ask for them.

Reading SHALL be limited to the caller's own firm, taken from their token, exactly as the export is. Reading the
record MUST NOT alter it, and MUST NOT be recorded as an action — otherwise looking at the log would grow the log.

#### Scenario: Newest first, a page at a time
- **WHEN** a FIRM_ADMIN reads the record with a page size smaller than the number of records
- **THEN** the newest records are returned with a way to ask for the next page, and following it reaches the older ones without repeating any

#### Scenario: Narrowed by person and kind
- **WHEN** the record is read for one person and one kind of action
- **THEN** only that person's actions of that kind are returned

#### Scenario: Another firm is not readable
- **WHEN** a FIRM_ADMIN reads the record while another firm has actions in the same period
- **THEN** none of them are returned, whatever parameters are given

#### Scenario: Reading leaves no trace
- **WHEN** the record is read
- **THEN** no new record is added and the chain head is unchanged

#### Scenario: Not an admin
- **WHEN** a user who is not a FIRM_ADMIN reads the record
- **THEN** the request is refused
