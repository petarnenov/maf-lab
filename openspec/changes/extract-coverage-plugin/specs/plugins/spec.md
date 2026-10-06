# Spec Delta

## ADDED Requirements

### Requirement: Coverage and test generation are a plugin
`coverage` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/coverage/` is deleted and the stack is rebuilt
- **THEN** nothing of `coverage` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: A test run with the plugin in use
- **WHEN** coverage is installed and an admin starts a test run
- **THEN** the run proceeds and is verified as before; without the plugin the coverage page and routes are absent
