# Spec Delta

## MODIFIED Requirements

### Requirement: Streaming chat with visible tool use
The `/chat` screen SHALL render the assistant's answer as it streams, show a
live card for each tool call (for example "searching documentation…",
"checking run 4417") that updates when the call finishes, and show a sources
panel with clickable sections. It SHALL render from the run's own events, and an
event it does not recognise SHALL leave the rest of the run rendering.

#### Scenario: Tool card lifecycle
- **WHEN** a tool call starts and later reports its result
- **THEN** a card appears in a running state and then shows the result summary and source count

#### Scenario: Sources panel
- **WHEN** a run reports the sources of its answer
- **THEN** each source is listed with its section path and a way to view its snippet

#### Scenario: An event the screen does not know
- **WHEN** a run carries an event this client has no rendering for
- **THEN** the answer, the tool cards and the sources still render
