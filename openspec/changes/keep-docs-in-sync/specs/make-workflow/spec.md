# Spec Delta

## MODIFIED Requirements

### Requirement: Target catalogue and help
The Makefile SHALL provide targets for lifecycle (`up`, `down`, `restart`, `ps`, `logs`, `clean`), data (`index`,
`reindex`, `drift`, `migrate`), quality (`test`, `test-dotnet`, `test-web`, `lint`, `verify`, `eval`, and
`eval-selection`, `eval-retrieval`, `eval-generation`, `eval-injection`), documentation (`docs`, `docs-check`), local
development (`dev`) and setup (`doctor`, `help`). `make help` SHALL list every target with a one-line description, and
README SHALL carry the same list, generated from the same descriptions.

#### Scenario: Help lists targets
- **WHEN** `make help` is run
- **THEN** every public target is listed with its description

#### Scenario: README lists the same targets
- **WHEN** `make docs-check` runs
- **THEN** it fails unless README's target list has exactly the targets and descriptions `make help` prints

#### Scenario: Documentation targets need no stack
- **WHEN** `make docs` or `make docs-check` runs on a machine with Python 3 and without Docker or the .NET SDK
- **THEN** it completes without starting any container or building any project

#### Scenario: Unknown target
- **WHEN** `make nonexistent` is run
- **THEN** make exits non-zero
