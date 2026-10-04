# Spec Delta

## MODIFIED Requirements

### Requirement: Local parity and caching
`make ci` SHALL run the same checks as the CI workflow (`ci.yml`) locally, including `make docs-check`. Workflows SHALL
cache NuGet and npm dependencies keyed on their lock/props files.

#### Scenario: Local CI
- **WHEN** a developer runs `make ci`
- **THEN** spec validation, the documentation check, .NET tests, web checks and the model-free e2e run in sequence and the exit code reflects the result
