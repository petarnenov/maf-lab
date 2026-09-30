## MODIFIED Requirements

### Requirement: Task input
A task SHALL carry the commit SHA to work at, the repo-relative target file, the target line coverage %, the maximum
number of attempts (at most 10), the model and the toolchain (`dotnet` or `vitest`). It MAY carry a token cap, a cost
cap, or both. A cap that is absent means that dimension is unlimited. A cap that is present SHALL be positive. The
agent SHALL reject input that is incomplete, names a file not in the repository at that commit, names a production
file under a test directory, asks for more than 10 attempts, or carries a cap that is zero or negative. The model's
API key SHALL come from the agent's own environment, never from the task.

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
