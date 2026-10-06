# Spec Delta

## ADDED Requirements

### Requirement: The assistant's A2A surface is a plugin
`a2a` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/a2a/` is deleted and the stack is rebuilt
- **THEN** nothing of `a2a` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: A partner question with the plugin in use
- **WHEN** a2a is in use and a partner asks for run 4417's status
- **THEN** the task completes in the 1.0 shape, as before
