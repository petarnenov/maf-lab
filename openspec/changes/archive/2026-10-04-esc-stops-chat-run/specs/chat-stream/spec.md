# Spec Delta

## MODIFIED Requirements

### Requirement: Answer runs remain stream-backed
When the user approves or rejects a pending proposal, the application SHALL create a streaming assistant turn for that
resume run before the answer request is sent. When that run is stopped — its terminal event is a `RUN_ERROR` with
`code` `abort` or a `RUN_FINISHED` whose outcome is `cancelled` — the proposal SHALL be waiting again, and answering
it again SHALL carry the same idempotency key as the stopped answer, so nothing is applied twice.

#### Scenario: Approving a proposal
- **WHEN** a conversation has a pending confirmation and the user approves it
- **THEN** the resume request starts a streaming assistant turn, the answer stream is processed through the normal chat
  event pipeline, and the behind-the-scenes panel remains available during the answer run

#### Scenario: Rejecting a proposal
- **WHEN** a conversation has a pending confirmation and the user rejects it
- **THEN** the resume request starts a streaming assistant turn, the answer stream is processed through the normal chat
  event pipeline, and the behind-the-scenes panel remains available during the answer run

#### Scenario: Stopping the answer run
- **WHEN** the user approves a pending proposal, presses Esc, and the answer run ends with a `RUN_ERROR` whose `code`
  is `abort`
- **THEN** the answer turn says it was stopped, the proposal shows as waiting again rather than gone or applied, and
  approving it again sends the same idempotency key as the stopped approval
