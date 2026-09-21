# Spec Delta

## MODIFIED Requirements

### Requirement: What the protocol does not name travels as a custom event
This system's own additions — the sources of an answer and the behind-the-scenes trace of a turn — SHALL be
carried as the protocol's custom events, each under a stable name, rather than as invented top-level event
types. A consumer that does not recognise a custom event SHALL be able to ignore it and still follow the run.
The web client SHALL use the protocol's own type constants (`EventType.*` from the AG-UI TypeScript SDK)
to discriminate all incoming events, so that a new protocol event type introduced in a future SDK version
is a compile error at the discrimination site rather than a silent fall-through to the default branch.

#### Scenario: Sources
- **WHEN** a turn has sources
- **THEN** they arrive as a custom event under a stable name, before the run ends

#### Scenario: The trace
- **WHEN** any turn runs
- **THEN** its trace events arrive as custom events while the turn runs, all before the run ends

#### Scenario: An unrecognised custom event
- **WHEN** a custom event's name is unknown to the client
- **THEN** it is ignored and the rest of the run still renders

#### Scenario: Event type discrimination uses SDK constants
- **WHEN** the web client processes a streamed event
- **THEN** the event's type is matched against `EventType.*` constants from `@ag-ui/core`, not against
  hand-rolled string literals, so any new protocol event type introduced in a future SDK version
  produces a TypeScript compile error rather than a silent fall-through
