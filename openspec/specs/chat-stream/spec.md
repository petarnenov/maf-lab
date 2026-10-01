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
The answer run that follows an approval or a rejection SHALL be followed by the same client path as an ordinary send,
so its tool calls and steps are shown in the conversation, and the monitor shows its trace while it runs, read from
the trace API like any other turn.

#### Scenario: Resume stream includes tool activity
- **WHEN** a resume run calls a tool and is read to completion
- **THEN** the conversation shows the tool call and the streamed answer text, and the monitor shows the run's trace
  events
