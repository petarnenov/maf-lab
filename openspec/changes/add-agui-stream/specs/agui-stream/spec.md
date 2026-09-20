# Spec Delta

## Purpose
The protocol between this system and whatever is watching an agent run: how a run starts, streams its answer and
its tool use, pauses when it needs a person, and ends — expressed in AG-UI rather than in names invented here.

## ADDED Requirements

### Requirement: A run is addressed by its thread and its run id
A request to run the agent SHALL carry a thread, a run and the messages of the turn, and MAY carry answers to
interrupts and client state. The thread SHALL be the server-issued conversation identifier bound to the caller;
a thread belonging to another principal SHALL be reported as not found, and a request without one SHALL start a
new thread whose identifier the caller learns from the run. Every event of a run SHALL name the run and the
thread it belongs to.

#### Scenario: A new thread
- **WHEN** a run is requested without a thread
- **THEN** a thread is created for the caller and its identifier is carried on the run's events

#### Scenario: Another principal's thread
- **WHEN** a run names a thread issued to a different user
- **THEN** the request is reported as not found and no run starts

#### Scenario: Every event says which run it belongs to
- **WHEN** a run streams
- **THEN** each event carries the same run identifier

### Requirement: A run begins and ends exactly once
A run SHALL begin with a run-started event and end with exactly one terminal event: finished when it completed
or was paused, or errored when it did not. Nothing SHALL follow the terminal event. When a run fails, the error
carried to the client SHALL be short user-facing text, with no internal detail.

#### Scenario: A complete run
- **WHEN** a turn answers a question
- **THEN** the stream starts with run-started, ends with run-finished, and nothing follows it

#### Scenario: A run that fails
- **WHEN** a turn fails
- **THEN** the stream ends with a run-error carrying short user-facing text and no stack trace, hostname or query internals

### Requirement: The answer streams as a text message
The assistant's answer SHALL be streamed as a text message: one start, then its content in order, then one end,
with every content event naming the message it belongs to. A run that produces no answer SHALL NOT open a text
message.

#### Scenario: A streamed answer
- **WHEN** the model produces an answer in several pieces
- **THEN** the client receives one text-message start, the pieces in order under the same message id, and one text-message end

#### Scenario: A turn with no answer
- **WHEN** a run ends without the model producing text
- **THEN** no text message was opened

### Requirement: A tool call streams as the protocol's tool events
Each tool call SHALL be streamed as a tool-call start naming the tool, its arguments, a tool-call end, and a
tool-call result, all sharing one tool-call identifier. The start SHALL be emitted before the tool executes.
Arguments and results that reach the client SHALL carry identifiers and summaries only, never free text a user
typed or a document's contents.

#### Scenario: Tool call lifecycle
- **WHEN** a turn calls a tool
- **THEN** the client receives tool-call start before the tool runs, then its arguments, then end, then the result, under one tool-call id

#### Scenario: A tool call's free text does not travel
- **WHEN** a tool is called with a query or a reason
- **THEN** the arguments the client receives name the identifiers and not the text

### Requirement: What the protocol does not name travels as a custom event
This system's own additions — the sources of an answer and the behind-the-scenes trace of a turn — SHALL be
carried as the protocol's custom events, each under a stable name, rather than as invented top-level event types.
A consumer that does not recognise a custom event SHALL be able to ignore it and still follow the run.

#### Scenario: Sources
- **WHEN** a turn has sources
- **THEN** they arrive as a custom event under a stable name, before the run ends

#### Scenario: The trace
- **WHEN** any turn runs
- **THEN** its trace events arrive as custom events while the turn runs, all before the run ends

#### Scenario: An unrecognised custom event
- **WHEN** a custom event's name is unknown to the client
- **THEN** it is ignored and the rest of the run still renders

### Requirement: A run that needs a person pauses
When a run needs something only a person can give, it SHALL finish as paused, carrying one interrupt: an
identifier, the sentence the person is asked, the shape of the answer expected, the tool call it belongs to and
the moment it stops being answerable. A paused run SHALL NOT have changed anything, and no further tool SHALL
execute in it.

#### Scenario: Pausing for an approval
- **WHEN** a turn proposes something that needs approval
- **THEN** the run finishes as paused with one interrupt carrying the question, the expected answer's shape, the tool call and the expiry — and nothing has been changed

#### Scenario: Nothing runs after the pause
- **WHEN** a run has paused
- **THEN** no tool executes in that run

### Requirement: An answer to an interrupt continues the run
A person's answer SHALL be sent as a new run that resumes the interrupt by its identifier. The system SHALL act
on the answer, and SHALL refuse an answer to an interrupt that was not issued to this caller, that has already
been answered, or that has expired — without acting on it.

#### Scenario: Approving
- **WHEN** a run resumes an interrupt with an approval
- **THEN** what the interrupt was about is carried out and the run reports it

#### Scenario: Declining
- **WHEN** a run resumes an interrupt with a refusal
- **THEN** nothing is carried out and the conversation continues

#### Scenario: Answering twice
- **WHEN** an interrupt that has already been answered is resumed again
- **THEN** the second answer is refused and nothing happens twice

#### Scenario: Someone else's interrupt
- **WHEN** a run resumes an interrupt issued to another user
- **THEN** it is refused and nothing is carried out

### Requirement: A run can be stopped
A caller SHALL be able to stop a run it started, and abandoning the stream SHALL have the same effect. A stopped
run SHALL end within one second, reporting that it was cancelled rather than that it succeeded, and no tool SHALL
execute after it was asked to stop. Which instance receives the request MUST NOT decide whether it works.

#### Scenario: Asking a run to stop
- **WHEN** a running turn is asked to stop
- **THEN** the run ends within one second, reporting that it was cancelled

#### Scenario: A stop that reaches the wrong replica
- **WHEN** a stop is sent to an instance that is not running that turn
- **THEN** the run is still stopped, because the instance asked the others

#### Scenario: Nothing runs after a stop
- **WHEN** a run is stopped while the model is deciding
- **THEN** no tool executes afterwards

#### Scenario: The client walks away
- **WHEN** the client abandons the stream
- **THEN** the run stops rather than continuing to completion
