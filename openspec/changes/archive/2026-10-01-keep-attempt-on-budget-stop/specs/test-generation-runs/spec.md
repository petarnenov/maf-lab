# Spec Delta

## MODIFIED Requirements

### Requirement: Run states
A run SHALL be in one of these states:

- `submitted` or `working`, mirrored from the A2A task;
- `verifying`, after the task completes, while the api checks the result;
- `candidate`, verified and on its branch, awaiting a decision;
- `accepted`, merged into `main`;
- `discarded`;
- `completed_no_change`, when the task completed with an empty diff;
- `failed` (with a reason), `canceled` or `verification_failed` (with a reason).

`accepted`, `discarded`, `completed_no_change`, `failed`, `canceled` and `verification_failed` SHALL be final.
Every run SHALL carry the attempt count, the latest coverage, the model, the budget, the tokens used and the cost.
The attempt count SHALL be the last attempt that started: an attempt the budget stops before it finishes still
counts, and the task's final report SHALL NOT lower the count.
When a task completes, the run SHALL keep the task's stop reason (`target`, `attempts` or `budget`) as its reason,
both for `completed_no_change` and for a candidate. A reason that verification later sets SHALL replace it.

#### Scenario: States in order
- **WHEN** a run reaches its target and passes verification
- **THEN** it has passed through `submitted`, `working`, `verifying` and is now `candidate` with reason `target`

#### Scenario: No change keeps why
- **WHEN** a task completes with an empty diff and stop reason `budget`
- **THEN** the run ends `completed_no_change` with reason `budget`

#### Scenario: Budget spent during the first attempt
- **WHEN** a task's budget is spent during attempt 1 and the task completes with no finished attempt
- **THEN** the run ends with attempt 1 and reason `budget`, not attempt 0

#### Scenario: Stopped before an attempt starts
- **WHEN** a task stops for `budget` before attempt 3, after attempt 2 finished
- **THEN** the run ends with attempt 2
