# Spec Delta

## ADDED Requirements

### Requirement: A stopped consultation is cancelled over there
When the run that is consulting a remote agent is stopped while the consultation is in flight, the assistant SHALL
cancel the remote task with A2A `tasks/cancel` for that task id, once it is known, and SHALL NOT leave it running.
A deadline that passes is not a stop: then the task SHALL be kept, as "A slow or absent agent does not become a hang"
says. The cancel SHALL be recorded as a consultation with its outcome, like any other (no content), and a cancel that
fails SHALL NOT hold up the run's end.

#### Scenario: Stopped during a review
- **WHEN** the compliance agent is reviewing an adjustment and the run that asked for the review is stopped
- **THEN** the assistant sends `tasks/cancel` for the review's task id, the review's task ends cancelled on the
  compliance agent, and the cancel is recorded with its outcome

#### Scenario: The deadline is not a stop
- **WHEN** a review outlives the consultation's deadline and the run was not stopped
- **THEN** no `tasks/cancel` is sent and the task id is kept so the answer can still be collected later

#### Scenario: The cancel itself fails
- **WHEN** the run is stopped during a review and the compliance agent cannot be reached for the cancel
- **THEN** the failed cancel is recorded and the run still ends promptly
