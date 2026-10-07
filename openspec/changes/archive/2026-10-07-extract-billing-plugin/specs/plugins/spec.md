# Spec Delta

## ADDED Requirements

### Requirement: Billing is a plugin
`billing` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/billing/` is deleted and the stack is rebuilt
- **THEN** nothing of `billing` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: A billing question with the plugin in use
- **WHEN** billing and its stores are installed and a user asks why run 4417 failed
- **THEN** the turn calls the billing server's tools through its manifest, exactly as before the move
