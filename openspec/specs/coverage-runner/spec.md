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

### Requirement: A test result is read only from a complete run
The runner SHALL NOT report a test run as passing unless the run's output shows that every test ran and passed.
When a child process prints more than the runner keeps, the runner SHALL keep both the beginning and the end of the
output, and SHALL mark in the kept text how many lines were left out, so that the summary a test run prints last is
never the part that is lost. For `dotnet`, the failed count SHALL be at least the number of failed tests the output
names, whatever the summary says. A run that built and printed no test summary SHALL be reported with at least one
failure, whose message says the run printed no summary, and never as zero failures.

#### Scenario: Output over the cap keeps its end
- **WHEN** a child process prints far more than the cap, starting with a first line and ending with `failed: 4`
- **THEN** the kept output starts with the first line, ends with `failed: 4`, says that lines were omitted, and stays
  within the cap

#### Scenario: Summary lost, failures named
- **WHEN** a `dotnet` run's output names two failed tests and carries no summary
- **THEN** the build is reported as ok and the run as having 2 failed tests

#### Scenario: No summary and nothing named
- **WHEN** a `dotnet` run builds and prints neither a summary nor any failed test
- **THEN** the run is reported with 1 failed test whose message says it printed no summary

### Requirement: Related tests only, on request
A request MAY name a test scope: `all` or `related`. Without one, the scope SHALL be `all`, and the runner SHALL run
the toolchain's whole unit suite as before. A `related` request without a target file SHALL be refused as invalid.

With `related`, the runner SHALL run only the related tests in the workspace after the diff is applied:

- every test file the diff adds or changes;
- every other test file that uses the target file or one of those changed test files.

For `dotnet`, a test file uses a file when it names a type that file declares. For `vitest`, a test file uses a file
when the file is in its import graph.

The runner SHALL run the whole suite instead, and SHALL say why, when either holds:

- the diff changes something the rule cannot follow: a file outside the toolchain's test sources, a project or package
  file, or the web test setup file;
- the rule selects no test.

Every result SHALL say what ran:

- the scope that was used;
- the test files of a `related` run;
- the reason when it fell back to `all`.

When a target file was named, the result SHALL also give that file's covered and uncovered line numbers, as measured
by this run.

#### Scenario: A new test file and the tests that use the target
- **WHEN** a `related` dotnet request adds `tests/Lab.Tests/CalcTests.cs`, and an existing `tests/Lab.Tests/UsesCalc.cs`
  names a type declared in the target file
- **THEN** the runner runs the classes of those two files only, and the result's scope is `related` with both files

#### Scenario: A change the rule cannot follow
- **WHEN** a `related` dotnet request changes the test project file
- **THEN** the runner runs the whole suite, and the result's scope is `all` with the reason

#### Scenario: Nothing selected
- **WHEN** a `related` request has no diff and no test file uses the target
- **THEN** the runner runs the whole suite, and the result says that nothing was selected

#### Scenario: Related without a target
- **WHEN** a request asks for `related` and names no target file
- **THEN** it is refused as invalid, and nothing runs

#### Scenario: Default scope
- **WHEN** a request names no scope
- **THEN** the whole suite runs, as before

#### Scenario: Line hits returned
- **WHEN** a request names a target file
- **THEN** the result lists the target's covered and uncovered line numbers from this run
