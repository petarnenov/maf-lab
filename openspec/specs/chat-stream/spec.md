# chat-stream Specification

## Purpose
Keeps the answer that follows an approved or rejected proposal on the same streaming path as any other chat turn, so
the user sees it arrive live and the behind-the-scenes monitor shows its trace and tool activity.

## Requirements

### Requirement: Answer runs remain stream-backed
When the user approves or rejects a pending proposal, the application SHALL create a streaming assistant turn for that
resume run before the answer request is sent.

#### Scenario: Approving a proposal
- **WHEN** a conversation has a pending confirmation and the user approves it
- **THEN** the resume request starts a streaming assistant turn, the answer stream is processed through the normal chat
  event pipeline, and the behind-the-scenes panel remains available during the answer run

#### Scenario: Rejecting a proposal
- **WHEN** a conversation has a pending confirmation and the user rejects it
- **THEN** the resume request starts a streaming assistant turn, the answer stream is processed through the normal chat
  event pipeline, and the behind-the-scenes panel remains available during the answer run

### Requirement: Resume events reach the monitor
The answer stream SHALL dispatch its AG-UI events through the same reducer path used for ordinary sends, so trace and
tool events from the resume run are visible in the monitor.

#### Scenario: Resume stream includes tool activity
- **WHEN** a resume response emits tool call and trace events and the stream is read to completion
- **THEN** the monitor receives those events and the resume turn renders the streamed answer text
