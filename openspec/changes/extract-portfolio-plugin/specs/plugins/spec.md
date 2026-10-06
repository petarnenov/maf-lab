# Spec Delta

## ADDED Requirements

### Requirement: Portfolio is a plugin
`portfolio` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/portfolio/` is deleted and the stack is rebuilt
- **THEN** nothing of `portfolio` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: A portfolio question with the plugin in use
- **WHEN** portfolio is installed and a user asks for A-1043's holdings
- **THEN** the turn calls `get_household_portfolio` through the plugin's manifest and shows the holdings card
