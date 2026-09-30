# Spec Delta

## ADDED Requirements

### Requirement: Documentation check on every push
The push workflow SHALL run `make docs-check` in the specs job, on every push and pull request. The workflow run MUST
fail when the check fails. The check MUST NOT need secrets, models, Docker or the .NET SDK.

#### Scenario: Doc left behind fails the run
- **WHEN** a pull request adds an api endpoint without a row in `docs/http-api.md`
- **THEN** the specs job fails with the check's message and the workflow run fails

#### Scenario: Green main includes docs
- **WHEN** the current main branch is pushed
- **THEN** the specs job's documentation check succeeds

## MODIFIED Requirements

### Requirement: Local parity and caching
`make ci` SHALL run the same checks as the push workflow locally, including `make docs-check`. Workflows SHALL cache
NuGet and npm dependencies keyed on their lock/props files.

#### Scenario: Local CI
- **WHEN** a developer runs `make ci`
- **THEN** spec validation, the documentation check, .NET tests, web checks and the model-free e2e run in sequence and the exit code reflects the result
