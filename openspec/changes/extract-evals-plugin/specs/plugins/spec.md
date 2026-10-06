# Spec Delta

## ADDED Requirements

### Requirement: Evals are a plugin
`evals` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/evals/` is deleted and the stack is rebuilt
- **THEN** nothing of `evals` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: An eval run with the plugin in use
- **WHEN** evals is installed and a developer runs `make eval SUITE=selection`
- **THEN** the suite runs on the installed plugins' cases as before
