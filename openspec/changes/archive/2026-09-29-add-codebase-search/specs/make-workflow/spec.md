# Spec Delta

## ADDED Requirements

### Requirement: Index the codebase
`make index-code` SHALL index the repository into the codebase collection. Documents that did not change SHALL be
skipped. `make index` and `make reindex` SHALL include the codebase. `make` SHALL index the codebase when its
collection is missing or empty, as it does for the other corpora. `make dev` SHALL run the codebase server locally.

#### Scenario: First start
- **WHEN** `make` runs with an empty codebase collection
- **THEN** the repository is indexed into it before the banner is printed

#### Scenario: Re-run
- **WHEN** `make index-code` runs again with no file changed
- **THEN** every document is reported unchanged and nothing is re-embedded
