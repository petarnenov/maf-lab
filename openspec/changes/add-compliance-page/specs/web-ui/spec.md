# Spec Delta

## ADDED Requirements

### Requirement: Compliance screen
The `/admin/compliance` screen SHALL be available to a FIRM_ADMIN and SHALL show three things for their own firm:
the state of the audit chain, the record of actions, and a way to produce an export.

The chain state SHALL be stated in words, not only in colour: whether it is intact, how many records were checked,
how many predate the chain, and — when it is broken — which record broke it and when. A broken chain MUST be
unmistakable, and the screen SHALL say that records before the break are unaffected.

The record SHALL be shown newest first with the time, the person, the kind, the action and its outcome, filterable
by person, kind and period, with a way to load older records. The screen MUST NOT imply that identifiers are the
whole story: where an action carries no arguments, it SHALL show that plainly rather than an empty cell.

The export SHALL take a period and, optionally, a person, download the package as a file, and then show the
manifest — the counts, the digest and the chain head — so it can be quoted without opening the file.

The screen SHALL NOT claim more than the system provides: it SHALL state that the chain detects tampering rather
than preventing it.

#### Scenario: Intact chain
- **WHEN** a FIRM_ADMIN opens the screen and the chain is intact
- **THEN** it says so in words, with how many records were checked and how many predate the chain

#### Scenario: Broken chain
- **WHEN** the chain is broken
- **THEN** the screen shows it unmistakably, names the record that broke it and the time, and says that earlier records are unaffected

#### Scenario: Browsing and filtering
- **WHEN** the admin filters by a person and loads more
- **THEN** only that person's actions are listed, newest first, and older ones are appended

#### Scenario: Export
- **WHEN** the admin exports a period
- **THEN** the package downloads as a file and the manifest's counts, digest and chain head are shown on screen

#### Scenario: Nothing recorded yet
- **WHEN** the record is empty for the chosen filters
- **THEN** the screen says so rather than showing an empty table

#### Scenario: Not an admin
- **WHEN** a user who is not a FIRM_ADMIN opens the screen
- **THEN** it shows the same access-denied treatment as the other admin screens
