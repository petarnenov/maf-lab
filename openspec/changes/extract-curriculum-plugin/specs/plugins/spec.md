# Spec Delta

## ADDED Requirements

### Requirement: Curriculum is a plugin
`curriculum` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/curriculum/` is deleted and the stack is rebuilt
- **THEN** nothing of `curriculum` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: The curriculum with the plugin in use
- **WHEN** curriculum is installed and a user opens `/curriculum`
- **THEN** the screen shows as before; without the plugin it is absent from the nav
