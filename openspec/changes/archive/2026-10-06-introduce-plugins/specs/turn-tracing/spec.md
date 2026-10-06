# Spec Delta

## MODIFIED Requirements

### Requirement: Complete turn trace
For every chat turn the system SHALL record an ordered trace of timestamped events and hand each event, as it is written, to the installed turn observers (the `monitor` plugin is one); with none installed, the trace exists only for the turn itself and its core record. An optional part of an event — the full model capture, the prompt's tool schemas, retrieval diagnostics — SHALL be built only when an observer asks for it. The trace MUST cover:
- turn start: principal, conversation, turn id, api replica, question;
- intent classification: the intent the turn proceeded with, whether retrieval was forced, the classifier's choice,
  its probability for every known intent and its confidence, the probability that the question is about the documented
  domain, the versioned model that answered, how long the classification took, and — when the turn proceeded with no
  recognised intent because of the classifier — why (below the confidence floor, outside the domain, timed out,
  rejected, unavailable). When tool routing is enabled it SHALL also carry the routing answer: each routing question's
  probability, the status answer, and either the routed tool with its arguments or why the turn was not routed. It
  MUST NOT contain any credential;
- the history window: included messages with roles, text and token counts, the token budget, and how many older
  messages were left out;
- the system prompt version and full text, and each offered tool with its description and input schema;
- every model call: iteration, full request messages and options (tool mode, temperature, reasoning setting), response
  text, requested tool calls, finish reason, input and output token usage when the provider reports it, latency,
  model and endpoint host;
- any tool call issued on the model's behalf — to force retrieval, or a routed read — with the reason it was issued;
- every tool call: full arguments, raw MCP result, error flag, latency and serving MCP replica;
- the exact data envelope the model received;
- the model's reasoning as ordered chunks (`reasoning.delta`, each with its character offset), coalesced from the
  reasoning the model streamed; concatenating them yields exactly what it reasoned. A model that reasons in
  several stretches across a turn SHALL have each recorded in the order it arrived;
- the streamed answer text as ordered chunks (`answer.delta`, each with its character offset). Chunks are coalesced
  from the model's text deltas, and concatenating them yields exactly the answer sent to the client;
- audit rows written, sources, production signals, memory rows stored, and turn end with total duration and any error.

#### Scenario: Procedural turn trace
- **WHEN** a user asks "What is the procedure when a fee schedule is missing?"
- **THEN** the trace contains, in order: turn start, intent (procedural, forced, with the classifier's confidence), history, prompt and tools, the forced search call, the MCP result with retrieval diagnostics, the envelope, at least one model call with its response, sources, signals, and turn end

#### Scenario: Intent decided by the model
- **WHEN** Jev classifies the question
- **THEN** the intent event names the versioned model, the choice, the probability of each known intent, the confidence and the duration

#### Scenario: Classification not used
- **WHEN** the classifier's confidence is below the floor, or it times out, fails or has no key
- **THEN** the intent event shows no recognised intent, nothing forced, and the reason

#### Scenario: Unknown tool attempt
- **WHEN** the model calls a tool that does not exist
- **THEN** the trace records the attempt, the refusal returned to the model, and the audit row

#### Scenario: Answer reconstructable from the trace
- **WHEN** a turn streams an answer
- **THEN** the `answer.delta` events have contiguous offsets starting at 0, and their texts concatenated equal the full answer

#### Scenario: Reasoning reconstructable from the trace
- **WHEN** a turn's model reasons before it answers
- **THEN** the `reasoning.delta` events have contiguous offsets starting at 0, their texts concatenated equal
  everything the model reasoned, and they are recorded before the model response that followed them

#### Scenario: A turn whose model does not reason
- **WHEN** a model answers without reasoning
- **THEN** the trace holds no `reasoning.delta` event and the rest of the trace is unchanged

#### Scenario: Reasoning stays out of the logs
- **WHEN** a turn's model reasons
- **THEN** no log line contains any of that reasoning

#### Scenario: Question outside the domain
- **WHEN** a procedurally-phrased question is outside the documented domain
- **THEN** the intent event shows the classifier's procedural choice, the in-domain probability, no recognised intent, nothing forced, and the reason

#### Scenario: Routed data turn
- **WHEN** routing is on and the user asks "status of run 4417"
- **THEN** the intent event shows the data intent, each routing probability and the routed tool with run id 4417, the routed call is recorded as issued on the model's behalf before any model call, and exactly one model call follows it

### Requirement: Live streaming of the trace
While the `monitor` plugin is installed, a turn's trace SHALL be readable while the turn runs, so that each step is visible when it happens. It SHALL NOT travel
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

### Requirement: Persistence and retention
While the `monitor` plugin is installed, the complete trace of each turn SHALL be kept in its table and returned by
`GET /api/turns/{turnId}/trace`; traces older than its configured retention (default 7 days) SHALL be deleted
automatically. Without the monitor the route does not exist and no complete trace is kept; the turn's core record is
kept with the turn either way.

#### Scenario: Reopen an old turn
- **WHEN** a user selects an earlier assistant turn of their conversation
- **THEN** its stored trace is shown

#### Scenario: Retention
- **WHEN** the retention job runs and a trace is older than the retention period
- **THEN** that trace is deleted and requesting it returns not found

### Requirement: The run's AG-UI frames are recorded
For every run it streams, while a turn observer asks for the frames (the `monitor` plugin does), the system SHALL record
each event it puts on the wire, in the order it was written, as
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

### Requirement: The frames are kept and served with the turn's trace
While the `monitor` plugin is installed, the frames of a run that produced a turn SHALL be stored with that turn's kept
trace and returned by
`GET /api/turns/{turnId}/trace` together with the trace's events. They SHALL be readable exactly by whoever may
read that turn's trace, and SHALL be deleted when that trace is deleted, whether by retention or otherwise. A run
that produces no turn — an answer to a confirmation — SHALL NOT have its frames stored, and the response for a
turn that has none SHALL say that they were not recorded rather than report an empty run.

#### Scenario: Reopen a stored turn
- **WHEN** a user opens an earlier assistant turn of their conversation
- **THEN** the trace response carries that turn's recorded frames alongside its trace events

#### Scenario: Another firm's admin
- **WHEN** a TENANT_ADMIN of tenant B requests a tenant A turn's trace
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

## ADDED Requirements

### Requirement: The turn's core record
Every turn SHALL keep, with the turn and for as long as its conversation, its core record: the events of its trace of
the kinds `intent`, `domain`, `boundary`, `guardrail`, `relevance`, `answer.check`, `signals`, `sources`, `audit`,
`focus`, `turn.end` and `envelope`, in the trace's own shape and under the same field cap, whatever plugin is
installed. `turn.end` SHALL carry the turn's number of model calls, and `relevance` the search's domain. The answer
check's previous read and the Jev and intent statistics SHALL read this record. The model's reasoning SHALL be kept with
the turn as its own field, with how long it took.

#### Scenario: Core only
- **WHEN** a turn runs with no turn observer installed
- **THEN** no live trace is written to the shared store, and the turn keeps its core record and its reasoning

#### Scenario: A follow-up is checked against what was read before
- **WHEN** a follow-up question is answered from what the previous turn read
- **THEN** the answer check reads the previous turn's envelopes from its core record
