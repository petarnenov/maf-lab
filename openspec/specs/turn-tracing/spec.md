# turn-tracing Specification

## Purpose
Captures a complete, structured trace of everything the system does for a chat turn, streams it live to the user who
asked, and keeps it for later inspection, so the internals of the agent and retrieval can be observed and learned from.

## Requirements

### Requirement: Complete turn trace
For every chat turn the system SHALL record an ordered trace of timestamped events. The trace MUST cover:
- turn start: principal, conversation, turn id, api replica, question;
- intent classification: the intent, whether retrieval was forced, which stage decided it (rules or model) and,
  when a model was asked, the model's raw answer and how long the classification took;
- the history window: included messages with roles, text and token counts, the token budget, and how many older
  messages were left out;
- the system prompt version and full text, and each offered tool with its description and input schema;
- every model call: iteration, full request messages and options (tool mode, temperature, reasoning setting), response
  text, requested tool calls, finish reason, input and output token usage when the provider reports it, latency,
  model and endpoint host;
- any tool call issued on the model's behalf to force retrieval;
- every tool call: full arguments, raw MCP result, error flag, latency and serving MCP replica;
- the exact data envelope the model received;
- the streamed answer text as ordered chunks (`answer.delta`, each with its character offset). Chunks are coalesced
  from the model's text deltas, and concatenating them yields exactly the answer sent to the client;
- audit rows written, sources, production signals, memory rows stored, and turn end with total duration and any error.

#### Scenario: Procedural turn trace
- **WHEN** a user asks "What is the procedure when a fee schedule is missing?"
- **THEN** the trace contains, in order: turn start, intent (procedural, forced, decided by the rules), history, prompt and tools, the forced search call, the MCP result with retrieval diagnostics, the envelope, at least one model call with its response, sources, signals, and turn end

#### Scenario: Intent decided by the model
- **WHEN** the rules recognise nothing and a model classifies the question
- **THEN** the intent event names the model stage, its duration and the raw answer it returned

#### Scenario: Unknown tool attempt
- **WHEN** the model calls a tool that does not exist
- **THEN** the trace records the attempt, the refusal returned to the model, and the audit row

#### Scenario: Answer reconstructable from the trace
- **WHEN** a turn streams an answer
- **THEN** the `answer.delta` events have contiguous offsets starting at 0, and their texts concatenated equal the full answer

### Requirement: Live streaming of the trace
Trace events SHALL be streamed to the requesting client while the turn runs, interleaved with the run's other
events, so that each step is visible when it happens. They SHALL travel as the event stream's extension point
rather than as a type of its own, so a consumer that does not know about them can still follow the run.

#### Scenario: Trace arrives before the answer completes
- **WHEN** a turn calls `search_documents`
- **THEN** the client receives the trace events for the tool call before the run's terminal event

#### Scenario: A consumer that ignores the trace
- **WHEN** a client drops the trace events
- **THEN** the run still renders from the protocol's own events

### Requirement: Persistence and retention
The complete trace of each turn SHALL be stored with the turn and returned by `GET /api/turns/{turnId}/trace`. Traces
older than the configured retention (default 7 days) SHALL be deleted automatically.

#### Scenario: Reopen an old turn
- **WHEN** a user selects an earlier assistant turn of their conversation
- **THEN** its stored trace is shown

#### Scenario: Retention
- **WHEN** the retention job runs and a trace is older than the retention period
- **THEN** that trace is deleted and requesting it returns not found

### Requirement: Access scope
A trace MUST only be readable by the user who owns the turn and, for turns in their firm's review queue, by a
FIRM_ADMIN of the same firm. Users of other firms MUST receive not found. Trace data MUST NOT be written to logs.

#### Scenario: Other user
- **WHEN** a different user of the same firm (not FIRM_ADMIN) requests another user's trace
- **THEN** the response is not found

#### Scenario: Other firm's admin
- **WHEN** a FIRM_ADMIN of firm B requests a trace of a firm A turn
- **THEN** the response is not found

#### Scenario: Logs stay clean
- **WHEN** a traced turn completes
- **THEN** no log line contains the question, the answer, snippets or prompt text

### Requirement: Size limits
Individual text fields in a trace SHALL be capped at 20,000 characters and a whole trace at 1 MB. Truncation MUST be
marked in the trace.

#### Scenario: Oversized field
- **WHEN** a field exceeds the cap
- **THEN** it is truncated and flagged as truncated in the event

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
