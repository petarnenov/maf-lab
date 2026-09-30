# Spec Delta

## MODIFIED Requirements

### Requirement: Target catalogue and help
The Makefile SHALL provide targets for lifecycle (`up`, `down`, `restart`, `ps`, `logs`, `clean`), data (`index`,
`reindex`, `drift`, `migrate`), quality (`test`, `test-dotnet`, `test-web`, `lint`, `verify`, `eval`,
`eval-selection`, `eval-retrieval`, `eval-generation`, `eval-injection`, and `coverage`), local development (`dev`)
and setup (`doctor`, `help`). `make help` SHALL list every target with a one-line description. `make coverage` SHALL
refresh the coverage snapshot at `main`'s commit through the running stack. It SHALL exit non-zero if the stack is
not up or the refresh fails.

#### Scenario: Help lists targets
- **WHEN** `make help` is run
- **THEN** every public target is listed with its description

#### Scenario: Unknown target
- **WHEN** `make nonexistent` is run
- **THEN** make exits non-zero

#### Scenario: Coverage refresh
- **WHEN** `make coverage` is run with the stack up
- **THEN** a new official snapshot for both toolchains is ingested and the target exits zero

#### Scenario: Coverage without the stack
- **WHEN** `make coverage` is run with the stack down
- **THEN** it exits non-zero and says the stack is not running
