# Spec Delta

## ADDED Requirements

### Requirement: A cancel stops the review wherever it runs
A `tasks/cancel` received by any reviewer replica SHALL stop the review, whichever replica is running it. The shared
task store SHALL be the one place a cancel is known: no replica SHALL need to be the one running the review to cancel
it, and no routing to a particular replica SHALL be required.
- Once a task is in a terminal state (`completed`, `canceled`, `failed`, `rejected`), the store SHALL NOT let any
  replica write a different state over it; the check and the write SHALL be one atomic operation in the store.
- The replica running a review SHALL notice a cancel recorded in the store within a short interval and stop: no further
  stage is started, no artifact is added, and the review does not complete.

#### Scenario: Cancelled through the other replica
- **WHEN** a review runs on one reviewer replica and `tasks/cancel` for its task is received by another
- **THEN** the task ends `canceled`, the running review stops without starting another stage, and the task is still
  `canceled` after the time the review would have taken

#### Scenario: A late write does not undo a cancel
- **WHEN** a task is `canceled` in the store and a replica then saves the same task as `working` or `completed`
- **THEN** the store keeps it `canceled`

#### Scenario: Finishing is not blocked
- **WHEN** a review completes without being cancelled
- **THEN** it is saved `completed` with its verdict, as before
