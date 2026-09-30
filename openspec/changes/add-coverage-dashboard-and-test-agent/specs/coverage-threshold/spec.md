# Spec Delta

## Purpose

Lets an administrator set the coverage each file must reach and, when a raise cannot be met by the tests that
exist, turns that raise into a confirmed, model-backed test-generation run.

## ADDED Requirements

### Requirement: Default and override
There SHALL be one global default line-coverage threshold, set in configuration, and each file MAY carry an
override. A file's effective threshold is its override if set, else the default. The UI SHALL show the effective
threshold and whether it is the default or an override. An administrator SHALL be able to clear an override,
returning the file to the default.

#### Scenario: Effective threshold
- **WHEN** the default is 80% and a file has no override
- **THEN** its effective threshold is shown as 80% (default)

#### Scenario: Clearing an override
- **WHEN** an administrator clears a file's 90% override
- **THEN** the file's effective threshold becomes the default

### Requirement: Saving without a run
Setting a threshold that is at or below the current value SHALL save immediately, without confirmation and without
starting a run. Raising a threshold to a value the file's current coverage already meets SHALL also save immediately.
The value SHALL be a whole percentage from 0 to 100. Any other value SHALL be rejected with nothing saved.

#### Scenario: Threshold lowered
- **WHEN** an administrator lowers a file's threshold from 80% to 70%
- **THEN** it saves at once, no dialog opens and no run starts

#### Scenario: Raised but already met
- **WHEN** a file is at 91% and an administrator raises its threshold from 80% to 90%
- **THEN** it saves at once and no run starts

#### Scenario: Out of range
- **WHEN** a threshold of 120 is submitted
- **THEN** it is rejected and the stored threshold is unchanged

### Requirement: Raising above coverage starts a confirmed run
Raising a file's threshold above its current coverage SHALL open a confirmation. The confirmation SHALL state the
current coverage, the target, and that an agent run will be started to write tests. After confirming, the
administrator SHALL choose a model and SHALL see the cost estimate (see `model-selection`) before the run can start.
The new threshold SHALL be saved only when the run has been accepted for execution. Cancelling at any step, or a run
that fails to start, SHALL leave the threshold unchanged. The raised threshold SHALL persist whatever the run's
eventual outcome.

#### Scenario: Happy path
- **WHEN** a file is at 62%, an administrator raises its threshold to 85%, confirms, picks a model and starts
- **THEN** the run is created, the threshold becomes 85% and the file shows the run as submitted

#### Scenario: Confirmation cancelled
- **WHEN** the administrator cancels at the confirmation or at the model picker
- **THEN** no run is created and the threshold stays as it was

#### Scenario: Agent unreachable at start
- **WHEN** the start request fails because the test agent cannot be reached
- **THEN** the UI says the agent is unavailable, no run is left active, and the threshold stays as it was

#### Scenario: Goal not reached keeps the threshold
- **WHEN** a run ends without reaching the target
- **THEN** the file keeps the raised threshold and is flagged as below it

### Requirement: One active run per file
A file SHALL have at most one active run (any state before a final one). While a run is active, the file's threshold
control SHALL be locked and SHALL show the run's status instead. The server SHALL refuse to start a second run for
that file, and SHALL refuse to change its threshold, whatever the UI shows.

#### Scenario: Run already active
- **WHEN** a start request arrives for a file that already has an active run
- **THEN** it is refused as a conflict, naming the active run, and nothing changes

#### Scenario: Control locked
- **WHEN** a file's run is working
- **THEN** its threshold control is disabled and shows the run's state and attempt
