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

### Requirement: Prerequisite checks
`make doctor` SHALL report, per prerequisite, whether Docker (with compose), the .NET SDK version pinned in
`global.json`, Node/npm and GNU make are available, whether `OLLAMA_API_KEY` and `JEV_MAF_LAB` are set, whether
`MAF_LAB_REPO` names a git repository, and whether the optional `GITHUB_ISSUES_TOKEN` is set, without printing any
secret value. Targets that need a missing tool SHALL fail early with a message naming the missing prerequisite.

#### Scenario: Missing API key
- **WHEN** `OLLAMA_API_KEY` is not set and `make doctor` runs
- **THEN** the report marks the key as missing and explains that chat needs it, and no key value is printed anywhere

#### Scenario: Missing Jev key
- **WHEN** `JEV_MAF_LAB` is not set and `make doctor` runs
- **THEN** the report marks it as missing and explains that intent classification needs it, and no key value is printed anywhere

#### Scenario: Missing tool
- **WHEN** `dotnet` cannot be found and `make test-dotnet` runs
- **THEN** it fails immediately with a message naming the .NET SDK

#### Scenario: No issue token
- **WHEN** `GITHUB_ISSUES_TOKEN` is not set and `make doctor` runs
- **THEN** the report marks it as optional and missing, and says that suspected bugs will be skipped without a GitHub
  issue
