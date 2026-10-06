# Spec Delta

## ADDED Requirements

### Requirement: Index admin is a plugin
`index-admin` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/index-admin/` is deleted and the stack is rebuilt
- **THEN** nothing of `index-admin` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: An index job with the plugin in use
- **WHEN** index-admin is in use and an admin starts a reindex
- **THEN** the job runs, reports progress and can be cancelled as before
