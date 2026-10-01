## MODIFIED Requirements

### Requirement: Live streaming of the trace
A turn's trace SHALL be readable while the turn runs, so that each step is visible when it happens. It SHALL NOT travel
on the run's AG-UI stream. The owner of the turn SHALL be able to read the trace recorded so far, and every event
recorded after a given position, from the trace API, under the same access rules as the stored trace. The run's own
progress SHALL travel on the stream as the protocol's steps (`STEP_STARTED`/`STEP_FINISHED`), named by what the turn
is doing (for example screening the question, calling a tool, checking the answer), so a client that never reads the
trace still sees what the turn is doing.

#### Scenario: Trace arrives before the answer completes
- **WHEN** a turn calls `search_documents` and the monitor is open
- **THEN** the monitor shows the trace events for that tool call before the run's terminal event

#### Scenario: A consumer that ignores the trace
- **WHEN** a client follows only the run's AG-UI stream
- **THEN** the run renders fully from the protocol's own events, and its steps show what the turn is doing

#### Scenario: The stream carries no trace
- **WHEN** any turn runs
- **THEN** no event on its AG-UI stream carries a trace event

#### Scenario: Another user's running turn
- **WHEN** a user asks for the live trace of a turn they do not own
- **THEN** it is reported as not found

### Requirement: The run's AG-UI frames are recorded
For every run it streams, the system SHALL record each event it puts on the wire, in the order it was written, as
a frame carrying: its position in the run, the milliseconds since the run started, the protocol event type, the
payload's size in bytes, and the payload itself. The record SHALL cover every event of the run, including those a
client may ignore — the run starting, a text message opening and closing, a tool call ending, a step — and the run's
terminal event.

The frame log SHALL be capped at 256 KB per run. Once the cap is reached, later frames SHALL still be recorded
with their position, timing, type and size, and SHALL be marked as truncated in place of their payload.

#### Scenario: Every event of a run is recorded
- **WHEN** a turn answers a question
- **THEN** the run's frame log holds, in order, the run-started frame, each step frame, the text message's start,
  content and end frames, each tool-call frame, each activity or state frame, and the terminal frame

#### Scenario: A trace frame is recorded by reference
- **WHEN** a turn records a trace event
- **THEN** no frame is recorded for it, because the trace no longer travels on the stream; the trace itself keeps it

#### Scenario: A run that exceeds the cap
- **WHEN** a run's frames exceed 256 KB
- **THEN** the frames past the cap are recorded with their position, timing and type, and are marked truncated
