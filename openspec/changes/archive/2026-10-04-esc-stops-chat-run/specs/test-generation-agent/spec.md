# Spec Delta

## ADDED Requirements

### Requirement: A cancel stops the run wherever it runs
A `tasks/cancel` received by any test agent replica SHALL stop the run, whichever replica holds the task's lease. The
shared task store SHALL be the one place a cancel is known; no routing to the replica running the task SHALL be
required.
- Once a task is in a terminal state, the store SHALL NOT let any replica write a different state over it; the check
  and the write SHALL be one atomic operation in the store.
- The replica running the task SHALL notice a cancel recorded in the store within a short interval and stop as "Stop
  conditions and states" says for a cancel: no further model call or test run is started. It SHALL then remove the
  task's checkpoint and give up its lease, so the task is not resumed.

#### Scenario: Cancelled through the other replica
- **WHEN** a run is in its attempt loop on one test agent replica and `tasks/cancel` for its task is received by another
- **THEN** the task ends `canceled`, the running replica starts no further model call or test run, its checkpoint and
  lease are gone, and the task is still `canceled` afterwards

#### Scenario: A late write does not undo a cancel
- **WHEN** a task is `canceled` in the store and the replica that ran it then saves it as `working` or `completed`
- **THEN** the store keeps it `canceled`
