# Spec Delta

## ADDED Requirements

### Requirement: Checks on every pull request and on main
A workflow SHALL run on every pull request, on every push to `main`, and when started by hand for any branch, with independent jobs for spec
validation, .NET build and tests, web lint/test/build, and an end-to-end stack test. The workflow run MUST fail if any
job fails.

A pull request's run SHALL test the branch merged with `main`. A push to a branch other than `main` SHALL NOT start
the workflow by itself, so a branch with an open pull request is checked once per commit, not twice.

#### Scenario: Green main
- **WHEN** the current main branch is pushed
- **THEN** the specs, dotnet, web and e2e jobs all succeed

#### Scenario: Broken test fails the run
- **WHEN** a commit makes a .NET or web test fail
- **THEN** the corresponding job and the workflow run fail

#### Scenario: One run per pull request commit
- **WHEN** a commit is pushed to a branch that has an open pull request
- **THEN** exactly one workflow run checks that commit, the pull request's

#### Scenario: A branch without a pull request
- **WHEN** a commit is pushed to a branch with no pull request
- **THEN** no workflow run starts until a pull request is opened or a run is started by hand

### Requirement: Documentation check on every pull request and on main
The CI workflow SHALL run `make docs-check` in the specs job, on every run: every pull request, every push to `main`,
and every run started by hand. The workflow run MUST
fail when the check fails. The check MUST NOT need secrets, models, Docker or the .NET SDK.

#### Scenario: Doc left behind fails the run
- **WHEN** a pull request adds an api endpoint without a row in `docs/http-api.md`
- **THEN** the specs job fails with the check's message and the workflow run fails

#### Scenario: Green main includes docs
- **WHEN** the current main branch is pushed
- **THEN** the specs job's documentation check succeeds

## REMOVED Requirements

### Requirement: Checks on every push and pull request
**Reason**: A push to a branch with an open pull request ran every job a second time on the same commit.
**Migration**: Replaced by "Checks on every pull request and on main". The jobs are the same; a branch without a pull
request is checked by opening one or starting the workflow by hand.

### Requirement: Documentation check on every push
**Reason**: It tied the documentation check to a trigger that no longer exists for branches.
**Migration**: Replaced by "Documentation check on every pull request and on main". The check itself is unchanged.
