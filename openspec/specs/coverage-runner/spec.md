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

### Requirement: Model-written changes are held to the lint bar CI applies
When a request carries a diff, the runner SHALL check the files the diff adds or changes against the lint CI applies
to them, and SHALL report every finding as a build diagnostic that names the file and line, with the build outcome
failed:

- for `dotnet`, each compiler or analyzer warning the build reports in one of those files, as a build with warnings
  as errors would fail on it;
- for `vitest`, each ESLint error the repository's ESLint configuration reports in one of those files, and each of
  those files that Prettier, with the repository's configuration, would format differently; the Prettier diagnostic
  SHALL name the first line that differs and say how Prettier writes it.

Warnings in files the diff does not add or change SHALL NOT fail the build. ESLint warnings SHALL NOT fail it. A
lint failure SHALL keep what the run measured (test counts, failures, the target file's coverage). When the lint
check cannot run, the build SHALL be reported failed with a diagnostic saying so. A request without a diff SHALL be
built, tested and measured without this check.

#### Scenario: A test that triggers CA2022
- **WHEN** a `dotnet` request's diff adds a test file whose build reports `warning CA2022` on line 9
- **THEN** the build is reported failed, its diagnostics carry that file, line and `warning CA2022`, and the test
  counts and the target file's coverage are still returned

#### Scenario: A warning outside the diff
- **WHEN** a `dotnet` build reports a warning in a file the diff does not add or change, and none in the diff's files
- **THEN** the build is reported ok and the warning is not among the diagnostics

#### Scenario: No diff, warnings present
- **WHEN** a request without a diff builds with warnings
- **THEN** the build is reported ok and the run is measured as before

#### Scenario: An ESLint error in a changed web test
- **WHEN** a `vitest` request's diff adds a test file with a variable that is assigned and never used
- **THEN** the build is reported failed with a diagnostic naming the file, the line and the ESLint rule

#### Scenario: A web test Prettier would format differently
- **WHEN** a `vitest` request's diff adds a test file that uses double quotes where the configuration asks for single
- **THEN** the build is reported failed with a diagnostic naming the file, the first line that differs, and that line
  as Prettier writes it

#### Scenario: Only ESLint warnings
- **WHEN** the ESLint configuration reports only warnings for a changed web file, and Prettier finds nothing
- **THEN** the build is reported ok

#### Scenario: The lint tools are missing
- **WHEN** a `vitest` request carries a diff and the web app's dependencies are not installed in the workspace
- **THEN** the build is reported failed with a diagnostic saying the lint check could not run

### Requirement: An identical request reuses a complete result
The runner SHALL answer a request from a result it computed earlier, without running anything, when all of these hold:

- the earlier request was identical: the same full 40-character commit id, the same toolchain, the same diff
  (compared by its SHA-256), the same target file and the same test scope (no scope is `all`);
- the earlier result is complete: status `ok` and no failed test. A failed build or a lint failure with every test
  passing is complete;
- it finished less than the reuse window ago (configurable, 15 minutes by default);
- the request does not ask for a fresh run.

A result that timed out, was rejected, failed to check out or restore, ended in a runner error, or has a failed test
SHALL never be reused. A request with an abbreviated commit id SHALL never be reused or stored for reuse. Results SHALL
only ever come from the runner's own runs: a caller cannot supply or change one.

A reused result SHALL be the earlier result unchanged, with what it was reused from: the id of the job that computed
it and when that job completed. A result the runner computed for the request SHALL carry neither.

While an identical request is queued or running, a new request SHALL wait for it instead of running a second copy, and
SHALL report that job's state and queue position. When that job's result can be reused, the waiting request SHALL get
it as a reused result. When it cannot, the waiting request SHALL be queued to run on its own. A request that asks for a fresh run SHALL neither wait for nor reuse another job, and its complete result SHALL replace
any kept one.

The runner SHALL keep at most a configured number of results for reuse (16 by default), dropping the oldest first, and
SHALL forget them on restart. For every request it SHALL log and count whether it was a hit, joined a running job, was
a miss, was a fresh run or could not be reused at all, with the toolchain and scope only and no request content.

#### Scenario: The same request twice
- **WHEN** a whole-suite request for a commit, a diff and a target completes green, and the same request comes 40
  seconds later
- **THEN** the second answer is that result, at once, naming the first job as the one it was reused from, and no test
  runs

#### Scenario: Any difference runs again
- **WHEN** a request differs from a completed one only in its diff, its target file, its scope, its toolchain or its
  commit
- **THEN** the runner runs it and the result is not marked reused

#### Scenario: A failure is not reused
- **WHEN** an identical request follows one that timed out, or one that had a failing test
- **THEN** the runner runs it again

#### Scenario: Two identical requests at once
- **WHEN** an identical request arrives while the first is still running
- **THEN** the runner runs the tests once, and both callers get the result, the second marked reused

#### Scenario: The waiting request runs when the first fails
- **WHEN** an identical request waits for one that then times out
- **THEN** the waiting request runs on its own

#### Scenario: The window has passed
- **WHEN** an identical request arrives after the reuse window
- **THEN** the runner runs it again

#### Scenario: A fresh run is asked for
- **WHEN** an identical request asks for a fresh run
- **THEN** the runner runs it, and the result is not marked reused

#### Scenario: Only so many results are kept
- **WHEN** more complete results have finished within the window than the runner keeps
- **THEN** the oldest is no longer reused
