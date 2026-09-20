# Spec Delta

## ADDED Requirements

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
