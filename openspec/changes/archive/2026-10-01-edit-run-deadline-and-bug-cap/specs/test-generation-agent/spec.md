# Spec Delta

## MODIFIED Requirements

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
