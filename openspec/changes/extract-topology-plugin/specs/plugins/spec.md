# Spec Delta

## ADDED Requirements

### Requirement: Topology is a plugin
`topology` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/topology/` is deleted and the stack is rebuilt
- **THEN** nothing of `topology` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: The topology with the plugin in use
- **WHEN** topology is installed and a user opens `/topology`
- **THEN** every installed service and domain server appears; no domain is named by the core
