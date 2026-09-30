## MODIFIED Requirements

### Requirement: Task input
A task SHALL carry the commit SHA to work at, the repo-relative target file, the target line coverage %, the maximum
number of attempts (at most 5), the model and the toolchain (`dotnet` or `vitest`). It MAY carry a token cap, a cost
cap, or both. A cap that is absent means that dimension is unlimited. A cap that is present SHALL be positive. The
agent SHALL reject input that is incomplete, names a file not in the repository at that commit, names a production
file under a test directory, asks for more than 5 attempts, or carries a cap that is zero or negative. The model's
API key SHALL come from the agent's own environment, never from the task.

#### Scenario: Too many attempts
- **WHEN** a task asks for 8 attempts
- **THEN** it is rejected as invalid before any model call

#### Scenario: No caps
- **WHEN** a task carries neither a token cap nor a cost cap
- **THEN** it is accepted, and only the attempt cap, a cancel or the caller's deadline ends it early

#### Scenario: Non-positive cap
- **WHEN** a task carries a cost cap of 0
- **THEN** it is rejected as invalid before any model call

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

## ADDED Requirements

### Requirement: Writing within the tool-round cap
An attempt SHALL have a configured cap on tool rounds (a model call and the tool calls it asks for). The attempt's
instructions SHALL state that cap and SHALL tell the model to write a test before it spends most of the rounds
reading. When 3 rounds remain, the agent SHALL tell the model, before its next call, how many rounds remain and that
it must write or improve a test file now. Reaching the cap SHALL end the attempt's model work, and the attempt SHALL
then be built and measured as usual.

#### Scenario: Cap stated
- **WHEN** an attempt starts with a cap of 12 rounds
- **THEN** its input states that it has 12 tool rounds

#### Scenario: Nudge before the cap
- **WHEN** the model has used 9 of 12 rounds and has not written a test file
- **THEN** its next call is told that 3 rounds remain and that it must write a test now

#### Scenario: Cap reached
- **WHEN** the model uses all 12 rounds
- **THEN** the attempt's model work ends and whatever it wrote is built and measured
