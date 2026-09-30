# coverage-runner Specification

## Purpose
An isolated service that builds and runs this repository's tests with coverage for a given commit plus a set of test
changes. It lets model-written code run without access to secrets or the internet.

## Requirements

### Requirement: Run tests with coverage for a commit and a diff
The runner SHALL accept a request naming a commit SHA, a toolchain (`dotnet` or `vitest`), an optional unified diff,
and an optional target file. For each request it SHALL use a clean workspace at that commit. It SHALL apply the diff,
build, run the toolchain's unit tests with coverage, and return:

- a build outcome with compiler diagnostics;
- the passed, failed and skipped test counts, with failure messages;
- the Cobertura report;
- when a target file was named, that file's line coverage and uncovered line ranges.

A diff that does not apply SHALL be reported as such, without running anything. For `dotnet`, only the unit test
project SHALL be run, not tests that need external services.

#### Scenario: Diff applied and measured
- **WHEN** the runner is given a commit, a diff adding one xUnit test file, and a target file
- **THEN** it returns the build outcome, test counts and the target file's coverage with its uncovered ranges

#### Scenario: Diff does not apply
- **WHEN** the diff conflicts with the commit
- **THEN** the runner reports that the diff does not apply and runs no build

#### Scenario: Clean workspace each time
- **WHEN** two requests run one after another at the same commit with different diffs
- **THEN** the second sees none of the first's changes

### Requirement: No secrets, no internet
The runner SHALL hold no API keys or signing secrets besides what it needs to check service tokens. Code it runs SHALL
have no route to the internet. It SHALL be reachable only on the internal network, by the test agent and the api, with
service credentials. It SHALL have read-only access to the repository.

#### Scenario: Test tries to call out
- **WHEN** a generated test tries to open a connection to an internet host
- **THEN** the connection fails and nothing leaves the internal network

#### Scenario: Test reads the environment
- **WHEN** a generated test enumerates environment variables
- **THEN** no model provider key or signing key is present

### Requirement: Bounded runs
Each request SHALL have a time limit. A request that exceeds it SHALL be stopped and reported as timed out. The
runner SHALL run a bounded number of requests at once and SHALL queue the rest. Queue position SHALL be reported to
the caller.

#### Scenario: Test hangs
- **WHEN** a generated test never returns
- **THEN** the run is stopped at the time limit and reported as timed out
