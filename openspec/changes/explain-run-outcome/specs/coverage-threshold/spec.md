## MODIFIED Requirements

### Requirement: Default and override
There SHALL be one global default line-coverage threshold, set in configuration, and each file MAY carry an
override. A file's effective threshold is its override if set, else the default. The UI SHALL show the effective
threshold and whether it is the default or an override. An administrator SHALL be able to clear an override,
returning the file to the default. When the default is above the file's current coverage, clearing the override
SHALL open the same confirmation as saving a threshold above coverage, with the default as the target. A run started
from it SHALL leave the file on the default, not on an override of the same value.

#### Scenario: Effective threshold
- **WHEN** the default is 80% and a file has no override
- **THEN** its effective threshold is shown as 80% (default)

#### Scenario: Clearing an override
- **WHEN** an administrator clears a file's 90% override and the file's coverage is 85%, at or above the 80% default
- **THEN** the file's effective threshold becomes the default at once, and no run starts

#### Scenario: Clearing an override below the default
- **WHEN** an administrator clears a file's 60% override, the default is 80% and the file is at 40%
- **THEN** the confirmation opens with 80% as the target, and a run started from it leaves the file on the default

### Requirement: Saving without a run
Saving a threshold that the file's current coverage already meets SHALL save immediately, without confirmation and
without starting a run, whether it lowers, keeps or raises the threshold. The value SHALL be a whole percentage from
0 to 100. Any other value SHALL be rejected with nothing saved. From the confirmation of a threshold above coverage,
the administrator SHALL be able to save the threshold without starting a run.

#### Scenario: Threshold lowered
- **WHEN** a file is at 75% and an administrator lowers its threshold from 80% to 70%
- **THEN** it saves at once, no dialog opens and no run starts

#### Scenario: Raised but already met
- **WHEN** a file is at 91% and an administrator raises its threshold from 80% to 90%
- **THEN** it saves at once and no run starts

#### Scenario: Saved without a run on purpose
- **WHEN** a file is at 40%, an administrator lowers its threshold from 90% to 85%, and chooses "Save without a run" in the confirmation
- **THEN** 85% is saved and no run starts

#### Scenario: Out of range
- **WHEN** a threshold of 120 is submitted
- **THEN** it is rejected and the stored threshold is unchanged

### Requirement: Raising above coverage starts a confirmed run
Saving a threshold above the file's current coverage SHALL open a confirmation, whether it raises, keeps or lowers
the stored threshold: a threshold above coverage means "reach it". The confirmation SHALL state the current
coverage, the target, and that an agent run will be started to write tests. After confirming, the administrator
SHALL choose a model and SHALL see the cost estimate (see `model-selection`) before the run can start. The new
threshold SHALL be saved only when the run has been accepted for execution. Cancelling at any step, or a run that
fails to start, SHALL leave the threshold unchanged. The saved threshold SHALL persist whatever the run's eventual
outcome.

#### Scenario: Happy path
- **WHEN** a file is at 62%, an administrator raises its threshold to 85%, confirms, picks a model and starts
- **THEN** the run is created, the threshold becomes 85% and the file shows the run as submitted

#### Scenario: Saving the threshold the file is below
- **WHEN** a file with no override is at 40% under the 80% default, and an administrator presses Save with 80%
- **THEN** the confirmation opens with 80% as the target, and a run started from it leaves the file on the default

#### Scenario: Lowered but still above coverage
- **WHEN** a file is at 40% and an administrator lowers its threshold from 90% to 85%
- **THEN** the confirmation opens with 85% as the target

#### Scenario: Confirmation cancelled
- **WHEN** the administrator cancels at the confirmation or at the model picker
- **THEN** no run is created and the threshold stays as it was

#### Scenario: Agent unreachable at start
- **WHEN** the start request fails because the test agent cannot be reached
- **THEN** the UI says the agent is unavailable, no run is left active, and the threshold stays as it was

#### Scenario: Goal not reached keeps the threshold
- **WHEN** a run ends without reaching the target
- **THEN** the file keeps the saved threshold and is flagged as below it
