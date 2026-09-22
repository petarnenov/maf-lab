# Spec Delta

## ADDED Requirements

### Requirement: The run's AG-UI frames are recorded
For every run it streams, the system SHALL record each event it puts on the wire, in the order it was written, as
a frame carrying: its position in the run, the milliseconds since the run started, the protocol event type, the
custom event's name when it has one, the payload's size in bytes, and the payload itself. The record SHALL cover
every event of the run, including those a client may ignore — the run starting, a text message opening and
closing, a tool call ending — and the run's terminal event.

A frame carrying a trace event SHALL be recorded by its name, the trace event's sequence number and its size,
without a second copy of the trace event's data, because the trace itself already carries it.

The frame log SHALL be capped at 256 KB per run. Once the cap is reached, later frames SHALL still be recorded
with their position, timing, type, name and size, and SHALL be marked as truncated in place of their payload.

#### Scenario: Every event of a run is recorded
- **WHEN** a turn answers a question
- **THEN** the run's frame log holds, in order, the run-started frame, the text message's start, content and end
  frames, each tool-call frame, each custom frame, and the terminal frame

#### Scenario: A trace frame is recorded by reference
- **WHEN** a turn emits a trace event
- **THEN** its frame is recorded with the trace event's sequence number and size, and without a copy of its data

#### Scenario: A run that exceeds the cap
- **WHEN** a run's frames exceed 256 KB
- **THEN** the frames past the cap are recorded with their position, timing and type, and are marked truncated

### Requirement: The frames are kept and served with the turn's trace
The frames of a run that produced a turn SHALL be stored with that turn and returned by
`GET /api/turns/{turnId}/trace` together with the trace's events. They SHALL be readable exactly by whoever may
read that turn's trace, and SHALL be deleted when that trace is deleted, whether by retention or otherwise. A run
that produces no turn — an answer to a confirmation — SHALL NOT have its frames stored, and the response for a
turn that has none SHALL say that they were not recorded rather than report an empty run.

#### Scenario: Reopen a stored turn
- **WHEN** a user opens an earlier assistant turn of their conversation
- **THEN** the trace response carries that turn's recorded frames alongside its trace events

#### Scenario: Another firm's admin
- **WHEN** a FIRM_ADMIN of firm B requests a firm A turn's trace
- **THEN** the response is not found, and no frame of that run is disclosed

#### Scenario: Retention
- **WHEN** the retention job deletes a turn's trace
- **THEN** that turn's frames are deleted with it

#### Scenario: A run with no turn
- **WHEN** a run answers a confirmation and so records no turn
- **THEN** its frames are not stored, and no stored turn reports them

#### Scenario: Frames stay out of the logs
- **WHEN** a run's frames are recorded
- **THEN** no log line contains a frame's payload
