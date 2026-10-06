# Spec Delta

## ADDED Requirements

### Requirement: Feedback review is a plugin
`feedback-review` SHALL be a plugin folder under `plugins/`, reached by the core only through its manifest and the core's seams; the core SHALL NOT name it outside that folder.

#### Scenario: The folder is deleted
- **WHEN** `plugins/feedback-review/` is deleted and the stack is rebuilt
- **THEN** nothing of `feedback-review` remains in the api, the web, the compose set or the docs, and `make test` and
  `make docs-check` pass

#### Scenario: Reviewing with the plugin in use
- **WHEN** feedback-review is in use and a tenant admin opens a flagged turn
- **THEN** the turn is shown and labelled as before
