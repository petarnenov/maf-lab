# Spec Delta

## MODIFIED Requirements

### Requirement: Applied at most once
Applying an adjustment SHALL be durable and SHALL survive a restart. The same proposal SHALL apply at most once,
however many times its confirmation arrives and whichever replica receives it. A repeated confirmation SHALL report
the adjustment as already applied, with the same identifier and the same resulting fee as the first.

A caller SHALL also be able to say, in its own terms, that two attempts are the same attempt: a write SHALL accept
an idempotency key the caller generated, and a repeat under that key SHALL be answered with the first answer
rather than done again. The keys a system has processed SHALL be remembered across replicas and for at least as
long as a caller could reasonably retry, and two different requests under one key SHALL be refused rather than
silently treated as the same.

#### Scenario: The same confirmation twice
- **WHEN** the same confirmed proposal is submitted twice
- **THEN** the fee moves once, the second answer says it was already applied, and both answers name the same adjustment

#### Scenario: Two replicas at once
- **WHEN** the same confirmed proposal reaches two replicas simultaneously
- **THEN** exactly one applies it and the other reports it as already applied

#### Scenario: An interrupted call sent again
- **WHEN** a write is sent again under the idempotency key of a call whose answer never arrived
- **THEN** the caller receives the first answer and nothing is applied a second time

#### Scenario: One key, two different requests
- **WHEN** a second, different write arrives under a key already used
- **THEN** it is refused, and the first write stands

#### Scenario: Applied adjustments outlive the process
- **WHEN** the service restarts after an adjustment was applied
- **THEN** the account's current fee still reflects it
