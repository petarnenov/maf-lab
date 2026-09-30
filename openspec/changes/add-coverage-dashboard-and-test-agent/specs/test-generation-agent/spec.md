# Spec Delta

## Purpose

An internal agent, reachable only over A2A, that writes tests for one source file of this repository until the
file reaches a coverage target or runs out of attempts. It returns what it did as a structured report and a diff.

## ADDED Requirements

### Requirement: A2A-only surface
The test-generation agent SHALL run as its own service and SHALL be reachable only over A2A: a discoverable agent
card and the A2A protocol endpoint. Callers SHALL authenticate with service credentials. The agent SHALL be reachable
only on the internal network, not through the public load balancer. The browser SHALL never reach it.

#### Scenario: Card discovery
- **WHEN** the api fetches the agent's well-known card on the internal network
- **THEN** it receives a card naming one skill, generating tests for one file to a coverage target

#### Scenario: Not published
- **WHEN** the agent's paths are requested through the public load balancer
- **THEN** they are not routed to the agent

#### Scenario: Missing credentials
- **WHEN** a task is sent without a valid service token
- **THEN** it is rejected as unauthorized

### Requirement: Task input
A task SHALL carry the commit SHA to work at, the repo-relative target file, the target line coverage %, the maximum
number of attempts (at most 5), the model, the toolchain (`dotnet` or `vitest`) and the run's token and cost caps. The
agent SHALL reject input that is incomplete, names a file not in the repository at that commit, names a production
file under a test directory, or asks for more than 5 attempts. The model's API key SHALL come from the agent's own
environment, never from the task.

#### Scenario: Too many attempts
- **WHEN** a task asks for 8 attempts
- **THEN** it is rejected as invalid before any model call

### Requirement: Attempt loop with feedback
An attempt SHALL be one cycle: generate or modify tests, build, run the relevant test suite with coverage, read the
target file's coverage, and compare it with the target. The agent SHALL feed each attempt's outcome into the next:
compiler errors, failing tests with their messages, guardrail violations, and the line ranges still uncovered.
Building and running SHALL go through the coverage runner, never in the agent's own process.

#### Scenario: Build error fed back
- **WHEN** attempt 1 fails to compile
- **THEN** attempt 2's input contains the compiler errors for the test files

#### Scenario: Uncovered ranges fed back
- **WHEN** attempt 2 reaches 74% of an 85% target
- **THEN** attempt 3's input lists the line ranges of the target file still uncovered

### Requirement: Tools with a test-only write allowlist
The agent SHALL have exactly these tools: read a file, list files, write or modify a file, run tests with coverage,
and read coverage for a file. Reading SHALL be limited to the repository at the task's commit. Writing SHALL be
limited to test locations: files under `tests/` for `dotnet`, and `*.test.ts` / `*.test.tsx` files or files under
`web/src/test/` for `vitest`. Production code SHALL be read-only. Paths SHALL be checked after normalisation, so `..`
segments, absolute paths and symlinks cannot escape the allowlist. A refused write SHALL be reported to the model as
a tool error and SHALL NOT end the run.

#### Scenario: Write to production code
- **WHEN** the model asks to write `src/Maf.Lab.Api/Program.cs`
- **THEN** the tool refuses, nothing is written, and the model is told writes are limited to test files

#### Scenario: Escape attempt
- **WHEN** the model asks to write `tests/../src/Maf.Lab.Api/Program.cs`
- **THEN** it is refused as outside the test allowlist

### Requirement: Test guardrails
The agent SHALL reject its own tests that are skipped or focused (`.skip`, `.only`, `xit`, `fit`, `[Fact(Skip=...)]`,
`[Theory(Skip=...)]`), that contain no assertion, or that make a test pass by catching the exception it is meant to
verify. A violating test SHALL count as a failed check in that attempt and SHALL be reported as feedback. The final
diff SHALL NOT contain a violation.

#### Scenario: Focused test
- **WHEN** a generated Vitest file contains `it.only(`
- **THEN** the attempt reports a guardrail violation and the next attempt is told to remove it

#### Scenario: Assertion-free test
- **WHEN** a generated xUnit test calls the method under test and asserts nothing
- **THEN** it is reported as assertion-free and the attempt does not count it

### Requirement: Stop conditions and states
The task SHALL move `submitted → working` and end in exactly one final state:

- `completed` with `goalReached=true` as soon as an attempt meets the target;
- `completed` with `goalReached=false` when all attempts are used, or when the token or cost cap would be exceeded;
- `failed` on an unrecoverable error (for example, the commit cannot be checked out, or the model is refused);
- `canceled` on a cancel request.

Progress updates SHALL report the current attempt n of N and the latest measured coverage.

#### Scenario: Target reached early
- **WHEN** attempt 2 reaches 86% of an 85% target
- **THEN** the task completes with `goalReached=true` and no third attempt runs

#### Scenario: Goal not reached after 5 attempts
- **WHEN** the fifth attempt ends at 79% of an 85% target
- **THEN** the task completes with `goalReached=false` and a report of all 5 attempts

#### Scenario: Budget exhausted
- **WHEN** the next attempt would exceed the run's cost cap
- **THEN** the task completes with `goalReached=false`, stop reason `budget`, and does not start that attempt

#### Scenario: Cancel mid-run
- **WHEN** a cancel request arrives during attempt 3
- **THEN** no further model call or test run is started, and the task ends `canceled`

### Requirement: Report and diff artifact
A completed task SHALL carry one artifact containing:

- the target and the final coverage;
- `goalReached` and the stop reason;
- a per-attempt log, each entry with coverage before and after, tests added or changed, errors and guardrail
  violations;
- the tokens used and the estimated cost;
- a unified diff of the test changes against the task's commit.

The diff SHALL touch only paths in the write allowlist. The artifact, task updates and telemetry SHALL NOT carry the
API key. Logs and telemetry SHALL NOT carry prompts or source text.

#### Scenario: Report shape
- **WHEN** a task completes
- **THEN** its artifact is one JSON document with those fields and a diff that applies cleanly to the task's commit
