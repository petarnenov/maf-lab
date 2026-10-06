# Spec Delta

## ADDED Requirements

### Requirement: Compliance is a plugin
`compliance` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/compliance/` is deleted and the stack is rebuilt
- **THEN** nothing of `compliance` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: A write with the reviewer in use
- **WHEN** compliance is in use and a user asks to credit A-1042
- **THEN** the fee adjustment is offered and reviewed as before; without compliance it is not offered
