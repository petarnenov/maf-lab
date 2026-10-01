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
attempt (1 to 40), the test runs the model may start per attempt (0 to 2) and the suspected bugs the run may report
(0 to 3); one that is absent SHALL take its default (40, 2 and 3). It MAY carry a token cap, a cost cap, or both. A cap
that is absent means that dimension is unlimited. A cap that is present SHALL be positive. The agent SHALL reject
input that is incomplete, names a file not in the repository at that commit, names a production file under a test
directory, asks for more than 10 attempts, carries a tool-round, test-run or suspected-bug limit outside its bounds, or
carries a cap that is zero or negative. The model's API key SHALL come from the agent's own environment, never from
the task.

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
- **WHEN** a task carries neither tool rounds, test runs nor suspected bugs
- **THEN** each attempt has 40 tool rounds, the model may start 2 test runs, and the run may report 3 suspected bugs

#### Scenario: Too many suspected bugs allowed
- **WHEN** a task asks for 4 suspected bugs
- **THEN** it is rejected as invalid before any model call

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

A run SHALL report at most the task's suspected-bug limit (3 when the task names none). The attempt's instructions SHALL
state that limit; with a limit of 0 they SHALL say the run reports no suspected bugs. A suspected-bug test SHALL still
contain assertions. Its lines do not count towards the file's coverage.

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
- **WHEN** an attempt would report a fourth suspected bug under the default limit
- **THEN** the fourth is reported as a guardrail violation, and the attempt does not count it

#### Scenario: A lower limit
- **WHEN** a task carries a suspected-bug limit of 1 and the model reports a second suspected bug
- **THEN** the tool does not record it, and the attempt's input states the limit of 1

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
  error lines. It SHALL also say what the attempt's measured run ran: the scope it used (`related` or `all`), the
  number of related test files, the number of tests that ran, the reason when the whole suite ran instead of the
  related tests, and whether the runner reused an earlier result. When the whole suite confirmed the attempt, it SHALL
  also carry that run's test counts, its coverage and whether it was reused. The entry's own test counts and coverage
  are then the confirmation's, as before;
- `text` and `reasoning`: the model's reply and, when the provider returns it, its reasoning. They SHALL be sent in
  chunks as they stream, no more than about every two seconds, with each chunk appended to the entry it continues;
- `stopped`: the task's work is over, with the stop reason, the last attempt that ran, the best coverage, and, for
  `budget`, the attempt not started. It SHALL be the last entry of a completed task;
- `resumed`: the agent took the task over after a restart, with the attempt it resumes at. Entries after it SHALL carry
  higher sequence numbers than every entry recorded before the restart.

The report's attempt lines SHALL carry the same scope and confirmation.

A text or reasoning entry SHALL be capped at 4 KB and marked truncated beyond it. Reporting SHALL NOT slow down or
fail the task: if an update cannot be sent, the agent SHALL go on and send later entries. None of this content SHALL
appear in the agent's logs, spans or metrics.

#### Scenario: A tool call is reported
- **WHEN** the model calls the run-tests tool in attempt 2 and the build succeeds with 12 passed, 1 failed at 72.1%
- **THEN** a `tool` entry for attempt 2 names the tool and carries build ok, 12 passed, 1 failed and 72.1%, and no source text

#### Scenario: A refused write is reported
- **WHEN** the model asks to write `src/Maf.Lab.Api/Program.cs`
- **THEN** a `tool` entry records the write as refused with its path and the reason, and the run continues

#### Scenario: A focused attempt confirmed on the whole suite
- **WHEN** attempt 2's measured run ran 58 tests from 3 related files, reached the target, and the whole suite then ran
  1219 tests green
- **THEN** the `attempt` entry carries scope `related`, 3 files and 58 tests, and a confirmation with 1219 passed. Its
  own test counts are the confirmation's

#### Scenario: The whole suite ran instead
- **WHEN** attempt 1 asked for the related tests but the runner ran the whole suite because nothing related was
  selected
- **THEN** the `attempt` entry carries scope `all` with that reason and no confirmation

#### Scenario: Model text streams
- **WHEN** the model streams a reply for eight seconds
- **THEN** the api receives the reply as several chunks of one `text` entry, not one message at the end

#### Scenario: The stop is reported
- **WHEN** the task stops for `budget` before attempt 3
- **THEN** the last entry is `stopped` with reason `budget`, last attempt 2 and attempt not started 3

#### Scenario: The resume is reported
- **WHEN** the agent restarts during attempt 3 and takes the task over
- **THEN** the next entry is `resumed` with attempt 3, numbered after the last entry recorded before the restart

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

### Requirement: A task survives a restart of the agent
A task SHALL belong to the shared store, not to the agent process running it. While a replica runs a task it SHALL
hold a lease on the task in the shared store and renew it; a lease that is not renewed SHALL lapse within one minute.
The agent SHALL keep a checkpoint of the task in the shared store: the request as soon as the task is accepted, and,
after the baseline and after every finished attempt, what the next attempt needs — the baseline coverage, the attempt
log so far, the best result with its diff and suspected bugs, the tests written so far, the feedback for the next
attempt, the tokens and cost used, and the last activity sequence number. The checkpoint SHALL NOT carry the API key.

On start, and then periodically, the agent SHALL look for tasks that are `submitted` or `working`, whose lease has
lapsed. It SHALL take each over (at most one replica at a time) and resume it from its checkpoint:
- the work of the last finished attempt SHALL be kept and the baseline SHALL NOT be measured again once recorded;
- an attempt that was interrupted SHALL run again from the end of the last finished attempt, as the same attempt
  number;
- the attempt cap, the budget (counting the tokens and cost already recorded) and the deadline SHALL apply to the task
  as a whole, not restart;
- the task SHALL end in the same final states as a task that ran without a restart.

A task that cannot be resumed — it has no checkpoint, or its workspace cannot be rebuilt at its commit — SHALL end
`failed` with reason `interrupted`. A task SHALL never stay `working` without a replica running it for longer than the
lease plus one takeover period. A canceled task SHALL NOT be resumed. The checkpoint and the lease SHALL be removed
when the task ends.

#### Scenario: Restart during an attempt
- **WHEN** the agent stops during attempt 2 of 10, after attempt 1 finished at 62%, and starts again
- **THEN** it takes the task over, records a `resumed` entry for attempt 2, runs attempt 2 from the tests attempt 1
  left, and the task goes on to a final state with a report whose attempt log holds attempt 1 once

#### Scenario: Restart during the baseline
- **WHEN** the agent stops while the baseline is being measured, and starts again
- **THEN** it takes the task over and measures the baseline, then runs attempt 1

#### Scenario: The budget carries over
- **WHEN** a task with a 100 000-token cap used 80 000 tokens before a restart, and the next attempt is estimated at
  30 000
- **THEN** after the takeover that attempt is not started and the task completes with stop reason `budget`

#### Scenario: Nothing to resume from
- **WHEN** a `working` task has no checkpoint and its lease has lapsed
- **THEN** the agent ends it `failed` with reason `interrupted`, and the api's run ends `failed` with reason
  `interrupted`

#### Scenario: A live task is not taken
- **WHEN** one replica is running a task and renewing its lease, and another replica looks for tasks to take over
- **THEN** the other replica leaves the task alone

#### Scenario: A canceled task stays canceled
- **WHEN** a task was canceled before the agent restarted
- **THEN** the agent does not resume it

### Requirement: Focused attempts, whole-suite baseline and confirmation
The agent SHALL measure the baseline on the whole suite, and SHALL keep the target file's covered and uncovered lines
from it.

Each attempt's measured run, and each `run_tests` call, SHALL ask the runner for the related tests only. The target
coverage the agent uses, shows and feeds back for such a run SHALL be computed from the lines:

- a line counts as covered when the baseline covered it or this run covered it;
- the percentage is covered lines over the baseline's executable lines;
- the uncovered ranges are what is left.

The production code does not change during a run, so tests the run did not select still cover what they covered at the
baseline. `run_tests` SHALL tell the model which tests ran and whether the run was focused.

When an attempt is clean on a focused run and reaches the target, the agent SHALL run the whole suite on that attempt's
diff before it stops, and SHALL report the `testing` phase while it does. That run's results SHALL replace the
attempt's:

- coverage measured, not merged;
- the test counts;
- the failures.

If the attempt is no longer clean on them, the loop SHALL go on, and those failures SHALL go to the next attempt as
feedback.

A run resumed from a checkpoint that has no baseline lines SHALL run its attempts and `run_tests` on the whole suite.

#### Scenario: An attempt measured on the related tests
- **WHEN** the baseline covered lines 1–4 of a 10-line target, and attempt 1's focused run covers lines 5–7
- **THEN** attempt 1's coverage is 70%, and its uncovered ranges are lines 8–10

#### Scenario: The model runs the tests
- **WHEN** the model calls `run_tests` in an attempt
- **THEN** the runner is asked for the related tests, and the result tells the model the scope and the test files that ran

#### Scenario: Reaching the target is confirmed on the whole suite
- **WHEN** a focused attempt is clean and reaches the target, and the whole suite on its diff is green
- **THEN** the run stops at the target, with the whole suite's coverage

#### Scenario: The whole suite finds a broken test
- **WHEN** a focused attempt is clean and reaches the target, but another test fails on the whole suite
- **THEN** the attempt is not clean, the run goes on, and the next attempt's input names the failing test

#### Scenario: Resumed from an older checkpoint
- **WHEN** a run resumes from a checkpoint without baseline lines
- **THEN** its attempts run the whole suite
