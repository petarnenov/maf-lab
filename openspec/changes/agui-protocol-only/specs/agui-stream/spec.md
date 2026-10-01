## ADDED Requirements

### Requirement: Only the protocol's own events travel
Every stream between an agent and the browser SHALL carry only the event types the AG-UI protocol defines. No
`CUSTOM` event SHALL be emitted or consumed. What this system adds to a run SHALL travel in one of the protocol's own
places:
- the tool-call result;
- an activity (`ACTIVITY_SNAPSHOT`/`ACTIVITY_DELTA`);
- shared state (`STATE_SNAPSHOT`/`STATE_DELTA`);
- a step (`STEP_STARTED`/`STEP_FINISHED`);
- the run's result or its interrupt.

Activity types and state fields are data inside those events, not new event types.

Events SHALL be produced by the official AG-UI server for agents and consumed by the official AG-UI client. The
system SHALL build an AG-UI event only inside a mapping hook registered with that server, and SHALL contain no code of
its own that writes or parses the stream's framing, or calls an agent endpoint except through that client. The build
SHALL fail when any of these appears.

#### Scenario: A chat turn uses only protocol events
- **WHEN** a turn searches the documents, shows a card and answers
- **THEN** every event on its stream has a type defined by the protocol, and none is `CUSTOM`

#### Scenario: A test-generation run uses only protocol events
- **WHEN** a run is replayed from start to end
- **THEN** every event on its stream has a type defined by the protocol, and none is `CUSTOM`

#### Scenario: A custom event is reintroduced
- **WHEN** a change adds code that emits a `CUSTOM` event, builds an AG-UI event outside a registered mapping hook, or
  parses the event stream itself
- **THEN** the build fails and names the offending file

### Requirement: Agents and screens can be swapped independently
A screen SHALL depend only on the protocol, not on which agent serves it. The same web code, unchanged, SHALL follow:
- the chat agent;
- the test-generation agent;
- any other agent that speaks AG-UI.

It SHALL show the agent's messages, tool calls, steps, state and run status. A screen MAY add a richer rendering for a
tool name or an activity type it knows. Without one, the item SHALL still be shown generically, and the run SHALL
still render to its end.

Likewise, an agent SHALL be servable to any AG-UI client: a generic client SHALL be able to start a run, follow it to its
terminal event, answer its interrupt and stop it, with no knowledge of this system.

#### Scenario: A new agent behind the same screen
- **WHEN** the chat screen is pointed at a trivial echo agent that this web code was never written for
- **THEN** the user's message is sent, the echo is shown as the answer, and the run ends as finished, without a change
  to the web code

#### Scenario: A tool the screen has no rendering for
- **WHEN** an agent calls a tool the screen has no rendering for
- **THEN** the tool call is shown generically with its name and that it finished, and the answer still renders

#### Scenario: A generic client drives our agent
- **WHEN** a generic AG-UI client with no knowledge of this system starts a chat run, receives an interrupt and
  resumes it
- **THEN** it receives a well-formed run that ends with exactly one terminal event, and the resumed run carries out
  the approved action

### Requirement: Sources travel in the search tool's result
The sources of an answer SHALL be carried in the tool-call result of the search that found them. That result SHALL
hold, for each source, its document id, its section path and its source path, and the summary and source count that
tool results carry today. The snippet shown for a source SHALL be no more than the snippet shown today. A run SHALL
NOT announce its sources in any other event.

#### Scenario: Sources of an answer
- **WHEN** a turn's search returns three sections that the answer cites
- **THEN** that search's tool-call result lists the three sources with their document ids and section paths, before
  the run ends

#### Scenario: A search with no results
- **WHEN** a search returns nothing
- **THEN** its tool-call result says so with a source count of zero, and no sources are shown

## MODIFIED Requirements

### Requirement: A run is addressed by its thread and its run id
A request to run the agent SHALL carry a thread, a run and the messages of the turn, and MAY carry answers to
interrupts and client state. The thread SHALL be the caller's conversation: a thread belonging to another principal SHALL
be reported as not found, a request without one SHALL start a new thread whose identifier the caller learns from the run,
and a well-formed thread identifier nobody has SHALL start a new thread under that identifier for the caller, since a
protocol client names its own threads. The run identifier SHALL also identify the turn the run records, so a client
knows its turn without being told. Every event of a run SHALL name the run and the thread it belongs to.

#### Scenario: A new thread
- **WHEN** a run is requested without a thread
- **THEN** a thread is created for the caller and its identifier is carried on the run's events

#### Scenario: A thread nobody has
- **WHEN** a run names a well-formed thread identifier that no conversation has
- **THEN** a conversation with that identifier is created for the caller and the run proceeds on it

#### Scenario: Another principal's thread
- **WHEN** a run names a thread issued to a different user
- **THEN** the request is reported as not found and no run starts

#### Scenario: Every event says which run it belongs to
- **WHEN** a run streams
- **THEN** each event carries the same run identifier

#### Scenario: The run names its turn
- **WHEN** a run answers a question
- **THEN** the turn it records has the run's identifier, and its trace is read under that identifier

### Requirement: A run can be stopped
A caller SHALL be able to stop a run it started by abandoning or aborting its stream through the protocol's client. A
stopped run SHALL end within one second, reporting that it was cancelled rather than that it succeeded, and no tool
SHALL execute after it was asked to stop. The system SHALL NOT offer a private stop endpoint beside the protocol.

#### Scenario: Asking a run to stop
- **WHEN** a running turn is aborted by its client
- **THEN** the run ends within one second, reporting that it was cancelled

#### Scenario: A stop that reaches the wrong replica
- **WHEN** a client aborts a run whose request is served by one replica while other replicas are running
- **THEN** the run on the replica serving the request is the one that stops, because the abort ends that very request;
  no other replica needs to be asked

#### Scenario: Nothing runs after a stop
- **WHEN** a run is stopped while the model is deciding
- **THEN** no tool executes afterwards

#### Scenario: The client walks away
- **WHEN** the client abandons the stream
- **THEN** the run stops rather than continuing to completion

### Requirement: A run can be rejoined from any replica
While a run is in progress its state SHALL be readable by every replica: the answer as it stands, the tool calls
made and how each ended, whether it is waiting for a person, and — once it is over — how it ended. A caller that
lost its stream SHALL be able to rejoin the run through any replica using the protocol's own means, and be given that
state as protocol events. The system SHALL NOT offer a private endpoint for it.

Asking about a run SHALL be subject to the same ownership as the thread it belongs to: a run of another
principal's thread SHALL be reported as not found. A run whose state is no longer kept SHALL be reported as not
found rather than as an empty run.

#### Scenario: The tab was closed
- **WHEN** a client abandons a run's stream and later rejoins that run through another replica
- **THEN** it is told, as protocol events, what the turn has said so far, which tools it called and how they ended

#### Scenario: A run that has finished
- **WHEN** a run has ended and is rejoined
- **THEN** it reports that it ended and how, rather than appearing still to be running

#### Scenario: A run that stopped for a person
- **WHEN** a run ended waiting for an approval and is rejoined
- **THEN** it ends paused with the same interrupt, naming what is waiting

#### Scenario: Someone else's run
- **WHEN** a run of another principal's thread is rejoined
- **THEN** it is reported as not found

#### Scenario: A run nobody kept
- **WHEN** a run older than the period its state is kept for is rejoined
- **THEN** it is reported as not found

## REMOVED Requirements

### Requirement: What the protocol does not name travels as a custom event
**Reason**: It made custom events the sanctioned extension point, so every agent grew its own events and its own
consumer, and neither side could be swapped. Replaced by "Only the protocol's own events travel".
**Migration**: Sources move to the search tool's result ("Sources travel in the search tool's result"). The trace
leaves the stream and is read from the trace API (`turn-tracing`). Event discrimination by the SDK's constants is now
done by the official client itself.
