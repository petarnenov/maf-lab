# Spec Delta

## ADDED Requirements

### Requirement: A cancel stops a task wherever it runs
A `tasks/cancel` received by any api replica SHALL stop the task, whichever replica runs it. The api's A2A task store
SHALL never let a later write replace a terminal state with another (one atomic statement), and a running task SHALL
notice a cancel recorded there within a short interval and stop.

#### Scenario: A billing run cancelled through the other replica
- **WHEN** a partner's billing run is in progress on one api replica and its cancel is received by the other
- **THEN** the task ends `canceled`, the run stops before its next stage, and the task is still `canceled` later

#### Scenario: A late write
- **WHEN** a task is `canceled` in the store and its run then saves it as `completed`
- **THEN** the store keeps it `canceled`
