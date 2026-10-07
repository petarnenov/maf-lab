# Spec Delta

## ADDED Requirements

### Requirement: Insights are a plugin
`insights` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/insights/` is deleted and the stack is rebuilt
- **THEN** nothing of `insights` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: The statistics with the plugin in use
- **WHEN** insights is installed and a developer opens the Jev statistics
- **THEN** the screen shows the numbers as before; without the plugin the screen and routes are absent
