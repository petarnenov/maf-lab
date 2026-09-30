# test-generation-agent Specification

## Purpose
An internal agent, reachable only over A2A, that writes tests for one source file of this repository until the
file reaches a coverage target or runs out of attempts. It returns what it did as a structured report and a diff.

## Requirements

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
number of attempts (at most 10), the model and the toolchain (`dotnet` or `vitest`). It MAY carry the tool rounds per
attempt (1 to 40) and the test runs the model may start per attempt (0 to 2); one that is absent SHALL take its
default (40 and 2). It MAY carry a token cap, a cost cap, or both. A cap that is absent means that dimension is
unlimited. A cap that is present SHALL be positive. The agent SHALL reject input that is incomplete, names a file not
in the repository at that commit, names a production file under a test directory, asks for more than 10 attempts,
carries a tool-round or test-run limit outside its bounds, or carries a cap that is zero or negative. The model's API
key SHALL come from the agent's own environment, never from the task.

#### Scenario: Too many attempts
- **WHEN** a task asks for 12 attempts
- **THEN** it is rejected as invalid before any model call

#### Scenario: No caps
- **WHEN** a task carries neither a token cap nor a cost cap
- **THEN** it is accepted, and only the attempt cap, a cancel or the caller's deadline ends it early

#### Scenario: Non-positive cap
- **WHEN** a task carries a cost cap of 0
- **THEN** it is rejected as invalid before any model call

#### Scenario: Ten attempts
- **WHEN** a task asks for 10 attempts
- **THEN** it is accepted, and progress reports attempt n of 10

#### Scenario: Too many tool rounds
- **WHEN** a task asks for 41 tool rounds per attempt
- **THEN** it is rejected as invalid before any model call

#### Scenario: Limits absent
- **WHEN** a task carries neither tool rounds nor test runs per attempt
- **THEN** each attempt has 40 tool rounds and the model may start 2 test runs

### Requirement: Attempt loop with feedback
An attempt SHALL be one cycle: generate or modify tests, build, run the relevant test suite with coverage, read the
target file's coverage, and compare it with the target. The agent SHALL feed each attempt's outcome into the next:
compiler errors, failing tests with their messages, guardrail violations, and the line ranges still uncovered. When
an attempt added or changed no test file, the next attempt's input SHALL say so, and SHALL say that reading without
writing does not count as progress. Building and running SHALL go through the coverage runner, never in the agent's
own process.

#### Scenario: Build error fed back
- **WHEN** attempt 1 fails to compile
- **THEN** attempt 2's input contains the compiler errors for the test files

#### Scenario: Uncovered ranges fed back
- **WHEN** attempt 2 reaches 74% of an 85% target
- **THEN** attempt 3's input lists the line ranges of the target file still uncovered

#### Scenario: An attempt that wrote nothing
- **WHEN** attempt 1 ends without writing any test file
- **THEN** attempt 2's input says that attempt 1 wrote no test, alongside the uncovered ranges

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
The agent SHALL reject its own tests that are focused (`.only`, `fit`, `fdescribe`), that contain no assertion, or
that make a test pass by catching the exception it is meant to verify. A check on a substitute's received calls
(`Received`, `DidNotReceive`, `ReceivedWithAnyArgs`, `DidNotReceiveWithAnyArgs`) SHALL count as an assertion. A
generated test SHALL NOT modify production code, either in its source or at run time (for example by writing to,
moving or deleting a file under `src/` or `web/src/`). The agent SHALL NOT skip a test (`.skip`, `xit`,
`[Fact(Skip=...)]`, `[Theory(Skip=...)]`), except a suspected-bug skip that follows the requirement below. A
violating test SHALL count as a failed check in that attempt and SHALL be reported as feedback. The final diff SHALL
NOT contain a violation.

#### Scenario: Focused test
- **WHEN** a generated Vitest file contains `it.only(`
- **THEN** the attempt reports a guardrail violation, and the next attempt is told to remove it

#### Scenario: Assertion-free test
- **WHEN** a generated xUnit test calls the method under test and asserts nothing
- **THEN** it is reported as assertion-free, and the attempt does not count it

#### Scenario: A received-call check is an assertion
- **WHEN** a generated xUnit test calls the method under test and ends with `await db.Received(1).HashSetAsync(...)` on a substitute
- **THEN** no assertion-free violation is reported for it

#### Scenario: Setting up a substitute is not an assertion
- **WHEN** a generated xUnit test only configures a substitute with `.Returns(...)` and calls the method under test
- **THEN** it is reported as assertion-free

#### Scenario: Skip without a suspected bug
- **WHEN** a generated test is skipped with no suspected-bug marker, or is not listed as a suspected bug in the report
- **THEN** it is reported as a skipped test, and the attempt does not count it

#### Scenario: Test writes to production code
- **WHEN** a generated test writes to a file under `src/`
- **THEN** it is reported as modifying production code, and the attempt does not count it

### Requirement: Suspected bugs are reported, not worked around
A test SHALL assert the behaviour the code is meant to have: what its names, documentation, specs and callers say. The
test SHALL NOT assert whatever the code happens to do. When such a test fails because the production code does not
behave as intended, the agent SHALL NOT change the production code, and SHALL NOT weaken the assertion to match the
observed behaviour. Instead it SHALL:

- keep the test with its assertion;
- mark it skipped with a suspected-bug marker (`suspected-bug: <title>` as the skip reason, or in a comment on the line
  before the skipped test);
- list it in the report as a suspected bug, with the test, its file, a title, a description, the expected and the
  actual behaviour, and the failure message.

A run SHALL report at most 3 suspected bugs. A suspected-bug test SHALL still contain assertions. Its lines do not count
towards the file's coverage.

#### Scenario: A bug is found
- **WHEN** a generated test expects `Pct(0, 0)` to be 100 and the code returns 0, and the name and documentation say
  100
- **THEN** the test keeps its assertion and is skipped with `suspected-bug: Pct of an empty file is 0`, the production
  code is untouched, and the report lists the suspected bug with expected and actual behaviour

#### Scenario: Assertion bent to the bug
- **WHEN** a test's assertion is changed to expect the buggy value so that it passes
- **THEN** this is the behaviour the requirement forbids; the agent's instructions forbid it, and the api's verification
  of suspected bugs does not rely on it

#### Scenario: Too many suspected bugs
- **WHEN** an attempt would report a fourth suspected bug
- **THEN** the fourth is reported as a guardrail violation, and the attempt does not count it

### Requirement: Stop conditions and states
The task SHALL move `submitted → working` and end in exactly one final state:

- `completed` with `goalReached=true` as soon as an attempt meets the target;
- `completed` with `goalReached=false` when all attempts are used, or, for a task that carries caps, when the token
  or cost cap would be exceeded;
- `failed` on an unrecoverable error (for example, the commit cannot be checked out, or the model is refused);
- `canceled` on a cancel request.

A task without caps SHALL never stop for `budget`. Progress updates SHALL report the current attempt n of N and the
latest measured coverage. Before it completes, the agent SHALL record a final `stopped` activity entry naming the
stop reason (`target`, `attempts` or `budget`), the last attempt that ran, and, for `budget`, the attempt it did not
start.

#### Scenario: Target reached early
- **WHEN** attempt 2 reaches 86% of an 85% target
- **THEN** the task completes with `goalReached=true`, no third attempt runs, and the last activity entry is `stopped` with reason `target`

#### Scenario: Goal not reached after 5 attempts
- **WHEN** the fifth attempt ends at 79% of an 85% target
- **THEN** the task completes with `goalReached=false`, a report of all 5 attempts, and a `stopped` entry with reason `attempts`

#### Scenario: Budget exhausted
- **WHEN** the next attempt would exceed the run's cost cap
- **THEN** the task completes with `goalReached=false`, stop reason `budget`, does not start that attempt, and records a `stopped` entry with reason `budget` naming the attempt not started

#### Scenario: Unlimited budget
- **WHEN** a task without caps has used 900 000 tokens after attempt 3 of 5
- **THEN** attempt 4 starts

#### Scenario: Cancel mid-run
- **WHEN** a cancel request arrives during attempt 3
- **THEN** no further model call or test run is started, and the task ends `canceled`

### Requirement: Report and diff artifact
A completed task SHALL carry one artifact containing:

- the target and the final coverage;
- `goalReached` and the stop reason;
- a per-attempt log, each entry with coverage before and after, tests added or changed, errors and guardrail
  violations;
- the suspected bugs, each with its test, test file, title, description, expected and actual behaviour, and failure
  message;
- the tokens used and the estimated cost;
- a unified diff of the test changes against the task's commit.

The diff SHALL touch only paths in the write allowlist. The artifact, task updates and telemetry SHALL NOT carry the
API key. Logs and telemetry SHALL NOT carry prompts or source text.

#### Scenario: Report shape
- **WHEN** a task completes
- **THEN** its artifact is one JSON document with those fields and a diff that applies cleanly to the task's commit

### Requirement: Activity reporting
While it works on a task, the agent SHALL report its activity to the api over A2A, as artifacts of the task, so that
the task as it stands holds every entry so far and a caller that reconnects or polls misses none. Each entry SHALL
carry a sequence number that increases within the task, the time, the attempt, and one kind:
- `phase`: the attempt entered `generating`, `building`, `testing` or `measuring`;
- `tool`: a tool call, with the tool name, the repository path it concerned (when any), and a structured outcome
  summary (for example build result, test counts and coverage for running tests, or refused with the reason for a
  write outside the allowlist). It SHALL never carry file contents or the diff;
- `attempt`: an attempt's result, with coverage before and after, the build outcome, test counts, and at most ten
  error lines;
- `text` and `reasoning`: the model's reply and, when the provider returns it, its reasoning. They SHALL be sent in
  chunks as they stream, no more than about every two seconds, with each chunk appended to the entry it continues;
- `stopped`: the task's work is over, with the stop reason, the last attempt that ran, the best coverage, and, for
  `budget`, the attempt not started. It SHALL be the last entry of a completed task.

A text or reasoning entry SHALL be capped at 4 KB and marked truncated beyond it. Reporting SHALL NOT slow down or
fail the task: if an update cannot be sent, the agent SHALL go on and send later entries. None of this content SHALL
appear in the agent's logs, spans or metrics.

#### Scenario: A tool call is reported
- **WHEN** the model calls the run-tests tool in attempt 2 and the build succeeds with 12 passed, 1 failed at 72.1%
- **THEN** a `tool` entry for attempt 2 names the tool and carries build ok, 12 passed, 1 failed and 72.1%, and no source text

#### Scenario: A refused write is reported
- **WHEN** the model asks to write `src/Maf.Lab.Api/Program.cs`
- **THEN** a `tool` entry records the write as refused with its path and the reason, and the run continues

#### Scenario: Model text streams
- **WHEN** the model streams a reply for eight seconds
- **THEN** the api receives the reply as several chunks of one `text` entry, not one message at the end

#### Scenario: The stop is reported
- **WHEN** the task stops for `budget` before attempt 3
- **THEN** the last entry is `stopped` with reason `budget`, last attempt 2 and attempt not started 3

#### Scenario: No content in telemetry
- **WHEN** the agent's logs and spans for a run are inspected
- **THEN** none contains the model's text, its reasoning or a tool's path summary

### Requirement: Writing within the tool-round cap
An attempt SHALL have a cap on tool rounds (a model call and the tool calls it asks for), taken from the task. The
attempt's instructions SHALL state that cap and SHALL tell the model to write a test before it spends most of the
rounds reading. When 3 rounds remain, the agent SHALL tell the model, before its next call, how many rounds remain and
that it must write or improve a test file now. Reaching the cap SHALL end the attempt's model work, and the attempt
SHALL then be built and measured as usual. The test runs the model may start within an attempt SHALL likewise come
from the task.

#### Scenario: Cap stated
- **WHEN** an attempt starts with a cap of 12 rounds
- **THEN** its input states that it has 12 tool rounds

#### Scenario: Nudge before the cap
- **WHEN** the model has used 9 of 12 rounds and has not written a test file
- **THEN** its next call is told that 3 rounds remain and that it must write a test now

#### Scenario: Cap reached
- **WHEN** the model uses all 12 rounds
- **THEN** the attempt's model work ends and whatever it wrote is built and measured

#### Scenario: Cap from the task
- **WHEN** a task carries 20 tool rounds per attempt
- **THEN** each attempt's input states 20 tool rounds and its tool loop stops after 20

#### Scenario: No test runs allowed
- **WHEN** a task carries 0 test runs per attempt and the model asks to run the tests
- **THEN** the tool refuses as over the limit, and the attempt's own build and measurement still run

### Requirement: Substitutes for interfaces
The `dotnet` test project SHALL provide a substitution library, and the coverage runner SHALL be able to build tests
that use it without network access. The agent's instructions for `dotnet` SHALL name that library as the way to
stand in for an interface the code under test depends on. They SHALL say that a large interface is substituted, not
implemented by hand.

#### Scenario: A class behind a large interface
- **WHEN** the agent works on a file whose class takes `StackExchange.Redis.IConnectionMultiplexer`
- **THEN** its instructions tell it to substitute the interface with the library, and a test that does so builds and runs in the coverage runner

#### Scenario: The runner has the package offline
- **WHEN** the coverage runner, which has no network, builds a diff that adds a test using `Substitute.For<IDatabase>()`
- **THEN** the build succeeds from the packages its image restored
