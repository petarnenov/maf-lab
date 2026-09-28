# Spec Delta

## MODIFIED Requirements

### Requirement: Prerequisite checks
`make doctor` SHALL report, per prerequisite, whether Docker (with compose), the .NET SDK version pinned in
`global.json`, Node/npm and GNU make are available, and whether `OLLAMA_API_KEY` and `JEV_MAF_LAB` are set, without
printing either value. Targets that need a missing tool SHALL fail early with a message naming the missing prerequisite.

#### Scenario: Missing API key
- **WHEN** `OLLAMA_API_KEY` is not set and `make doctor` runs
- **THEN** the report marks the key as missing and explains that chat needs it, and no key value is printed anywhere

#### Scenario: Missing Jev key
- **WHEN** `JEV_MAF_LAB` is not set and `make doctor` runs
- **THEN** the report marks it as missing and explains that intent classification needs it, and no key value is printed anywhere

#### Scenario: Missing tool
- **WHEN** `dotnet` cannot be found and `make test-dotnet` runs
- **THEN** it fails immediately with a message naming the .NET SDK
