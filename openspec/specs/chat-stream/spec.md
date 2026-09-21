# chat-stream Specification

## Purpose
TBD - created by archiving change fix-behind-scenes-panel-stream. Update Purpose after archive.

## Requirements

### Requirement: Answer runs remain stream-backed
When the user approves or rejects a pending proposal, the application must create a streaming assistant turn for that resume run before the answer request is sent.

#### Scenario: Approving a proposal
- Given a conversation has a pending confirmation
- When the user approves it
- Then the resume request starts a streaming assistant turn
- And the answer stream is processed through the normal chat event pipeline
- And the behind-the-scenes panel remains available during the answer run

#### Scenario: Rejecting a proposal
- Given a conversation has a pending confirmation
- When the user rejects it
- Then the resume request starts a streaming assistant turn
- And the answer stream is processed through the normal chat event pipeline
- And the behind-the-scenes panel remains available during the answer run

### Requirement: Resume events reach the monitor
The answer stream must dispatch its AG-UI events through the same reducer path used for ordinary sends, so trace and tool events from the resume run are visible in the monitor.

#### Scenario: Resume stream includes tool activity
- Given a resume response emits tool call and trace events
- When the stream is read to completion
- Then the monitor receives those events
- And the resume turn renders the streamed answer text
