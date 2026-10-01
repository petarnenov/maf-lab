## MODIFIED Requirements

### Requirement: Resume events reach the monitor
The answer run that follows an approval or a rejection SHALL be followed by the same client path as an ordinary send,
so its tool calls and steps are shown in the conversation, and the monitor shows its trace while it runs, read from
the trace API like any other turn.

#### Scenario: Resume stream includes tool activity
- **WHEN** a resume run calls a tool and is read to completion
- **THEN** the conversation shows the tool call and the streamed answer text, and the monitor shows the run's trace
  events
