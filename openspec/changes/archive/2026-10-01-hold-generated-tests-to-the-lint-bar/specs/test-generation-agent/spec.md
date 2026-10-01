# Spec Delta

## MODIFIED Requirements

### Requirement: Attempt loop with feedback
An attempt SHALL be one cycle: generate or modify tests, build, run the relevant test suite with coverage, read the
target file's coverage, and compare it with the target. The agent SHALL feed each attempt's outcome into the next:
compiler errors, lint diagnostics, failing tests with their messages, guardrail violations, and the line ranges still
uncovered. When an attempt added or changed no test file, the next attempt's input SHALL say so, and SHALL say that
reading without writing does not count as progress. Building and running SHALL go through the coverage runner, never
in the agent's own process.

The agent's rules SHALL say that its files are held to the lint bar CI applies: for `dotnet` every compiler or
analyzer warning in them fails the build, and for `vitest` they must have no ESLint errors and must be formatted as
Prettier formats them, with the repository's Prettier settings named. When an attempt's result or a run-tests result
carries lint diagnostics, the next attempt's input and the run-tests result SHALL say that these fail the build as
they fail CI.

#### Scenario: Build error fed back
- **WHEN** attempt 1 fails to compile
- **THEN** attempt 2's input contains the compiler errors for the test files

#### Scenario: A warning fed back as an error
- **WHEN** attempt 1's test file builds with `warning CA2022` and the runner reports the build failed for it
- **THEN** attempt 2's input carries the CA2022 diagnostic and says that warnings in its files fail the build, as in CI

#### Scenario: The rules state the lint bar
- **WHEN** the agent starts a task
- **THEN** its rules say that warnings fail the build and that web tests must pass ESLint and Prettier with the
  repository's settings

#### Scenario: Uncovered ranges fed back
- **WHEN** attempt 2 reaches 74% of an 85% target
- **THEN** attempt 3's input lists the line ranges of the target file still uncovered

#### Scenario: An attempt that wrote nothing
- **WHEN** attempt 1 ends without writing any test file
- **THEN** attempt 2's input says that attempt 1 wrote no test, alongside the uncovered ranges
