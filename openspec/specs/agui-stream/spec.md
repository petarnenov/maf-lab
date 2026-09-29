# agui-stream Specification

## Purpose
The protocol between this system and whatever is watching an agent run: how a run starts, streams its answer and
its tool use, pauses when it needs a person, and ends — expressed in AG-UI rather than in names invented here.

## Requirements

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
typed or a document's contents. The only exception is a tool result carried by an activity card, under the
requirement for data cards.

#### Scenario: Tool call lifecycle
- **WHEN** a turn calls a tool
- **THEN** the client receives tool-call start before the tool runs, then its arguments, then end, then the result, under one tool-call id

#### Scenario: A tool call's free text does not travel
- **WHEN** a tool is called with a query or a reason
- **THEN** the arguments the client receives name the identifiers and not the text

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

### Requirement: A run can be rejoined from any replica
While a run is in progress its state SHALL be readable by every replica: the answer as it stands, the tool calls
made and how each ended, whether it is waiting for a person, and — once it is over — how it ended. A caller that
lost its stream SHALL be able to ask for the run by its identifier through any replica and be given that state.

Asking about a run SHALL be subject to the same ownership as the thread it belongs to: a run of another
principal's thread SHALL be reported as not found. A run whose state is no longer kept SHALL be reported as not
found rather than as an empty run.

#### Scenario: The tab was closed
- **WHEN** a client abandons a run's stream and later asks for that run through another replica
- **THEN** it is told what the turn has said so far, which tools it called and how they ended

#### Scenario: A run that has finished
- **WHEN** a run has ended and is asked for
- **THEN** it reports that it ended and how, rather than appearing still to be running

#### Scenario: A run that stopped for a person
- **WHEN** a run ended waiting for an approval and is asked for
- **THEN** it says so, and names what is waiting

#### Scenario: Someone else's run
- **WHEN** a run of another principal's thread is asked for
- **THEN** it is reported as not found

#### Scenario: A run nobody kept
- **WHEN** a run older than the period its state is kept for is asked for
- **THEN** it is reported as not found

### Requirement: A typed tool result travels as a data card
A tool whose declared result type has no free-text field MAY have its successful result sent to the client as an
activity (`ACTIVITY_SNAPSHOT`).

**Which tools.** The tools SHALL be named on a fixed allow-list in the server, each with its activity type:
- `get_household_portfolio` as `maf-lab/holdings`;
- `get_aum_history` as `maf-lab/aum-history`;
- `list_my_accounts` as `maf-lab/accounts`.

A tool not on the list SHALL NOT produce an activity.

**The event.**
- It SHALL be emitted once per such call, after that call's tool-call result and before the run ends.
- Its message id SHALL be derived from the tool-call id.
- Its content SHALL be the tool's structured result, as the tool's own firm-scoped read produced it.

**When there is no card.** No activity SHALL be emitted for:
- a result that failed;
- a result that is not structured;
- a result the guardrail withheld.

**Only numbers, flags and names.** The content SHALL carry no free text from a user, a document or a record note: only
numbers, flags, enumerations, dates, identifiers and the names of the firm's own accounts and households. The
tool-call result event itself SHALL stay a summary.

#### Scenario: A holdings card
- **WHEN** a turn calls `get_household_portfolio` for A-1043 and it succeeds
- **THEN** after the tool-call result the client receives one activity of type `maf-lab/holdings`, whose content holds
  the account id, the holdings with weights, drift and plan, the total and the currency

#### Scenario: A tool outside the allow-list
- **WHEN** a turn calls `search_documents`
- **THEN** no activity is emitted, and its tool-call result is a summary as before

#### Scenario: A failed or withheld result
- **WHEN** `get_household_portfolio` returns an error, or its result is withheld by the guardrail
- **THEN** no activity is emitted for that call

#### Scenario: Another firm's account
- **WHEN** a firm-b user asks for the portfolio of A-1042
- **THEN** the tool returns its not-found error and no activity is emitted

### Requirement: The account in focus travels as shared state
A run SHALL carry the conversation's account in focus as AG-UI shared state, of the shape
`{ focus: { accountId } | null }`.

**Server to client.**
- The server SHALL emit a `STATE_SNAPSHOT` with the state the turn starts with, right after the run starts.
- It SHALL emit another whenever a tool read changes the focus during the run.

**Client to server.**
- The client MAY send its current state as `RunAgentInput.state`.
- The server SHALL accept a sent `focus.accountId` only when that id appeared in a data card this conversation showed.
  Such an id counts as offered.
- The server SHALL accept `focus: null` as clearing the focus.
- Any other value SHALL be ignored, the stored focus SHALL stand, and the trace SHALL record the rejection without the
  sent value.
- When no state is sent, the stored focus SHALL be used.

The state SHALL carry an account id only: no name, no holdings and no text from the user or the model.

#### Scenario: State at the start of a run
- **WHEN** a conversation whose focus is A-1043 starts a run
- **THEN** the client receives `STATE_SNAPSHOT { focus: { accountId: "A-1043" } }` right after `RUN_STARTED`

#### Scenario: A read moves the focus
- **WHEN** a turn in that conversation reads the portfolio of A-1042 successfully
- **THEN** the client receives a `STATE_SNAPSHOT` with `A-1042` in focus before the run ends

#### Scenario: The client picks an account it was shown
- **WHEN** the client sends `state: { focus: { accountId: "A-1044" } }` and A-1044 was listed in an accounts card of
  this conversation
- **THEN** the turn runs with A-1044 in focus, and the starting snapshot says so

#### Scenario: The client sends an account it was never shown
- **WHEN** the client sends `state: { focus: { accountId: "B-200" } }` in a firm-a conversation that never showed B-200
- **THEN** the sent focus is ignored, the stored focus stands, and the trace records a rejected focus without the id

#### Scenario: Clearing the focus
- **WHEN** the client sends `state: { focus: null }`
- **THEN** the conversation has no account in focus, and the starting snapshot says `focus: null`
