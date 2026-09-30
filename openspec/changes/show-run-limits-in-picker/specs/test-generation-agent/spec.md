# Spec Delta

## MODIFIED Requirements

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
