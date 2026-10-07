# Spec Delta

## ADDED Requirements

### Requirement: Observability is a plugin
`observability` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/observability/` is deleted and the stack is rebuilt
- **THEN** nothing of `observability` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: Telemetry with the plugin in use
- **WHEN** observability is installed and a user opens `/telemetry`
- **THEN** the screen shows the stack's numbers as before; without the plugin nothing is exported and the screen is absent
