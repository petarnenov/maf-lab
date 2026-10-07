## ADDED Requirements

### Requirement: A write tool asks before it writes
A tool that writes SHALL, when called without a confirmation, return the protocol's request for input (MCP's
multi-round-trip `input_required`) and SHALL NOT write. The request SHALL carry a summary fit for a person to check, an opaque state that
identifies the proposal, and when the proposal stops being answerable. The core SHALL read none of the summary's
fields: it keeps the summary, shows it and hands it back.

#### Scenario: The first call writes nothing
- **WHEN** a write tool is called without a confirmation
- **THEN** the result is a request for input carrying the summary, the state and the expiry, and nothing is written

#### Scenario: A summary the core has never seen
- **WHEN** a plugin's write tool sends a summary with fields no other tool uses
- **THEN** the proposal is kept and shown with those fields, and the core needed no change to do so

### Requirement: Each write tool has its own flow
Between a tool's request for input and the person, the core SHALL hand the proposal to the write-confirmation flow
contributed for that tool's name, together with the conversation's proposals of that tool still waiting for input. The
flow SHALL decide whether the person is asked to confirm, the model is told to ask the person for input first (the
proposal then waits for that input), or the model is told why not. The flow SHALL own the schema that describes its
summary. A write tool with no flow SHALL be refused: the model is told the write cannot be confirmed, no person is
asked, and nothing is written.

#### Scenario: A flow asks the person
- **WHEN** a write tool asks for input and its flow decides the person should confirm
- **THEN** the run pauses with the flow's question and the summary, and no write has happened

#### Scenario: A flow needs input first
- **WHEN** a flow decides the person must answer a question before anything is confirmed
- **THEN** the model is told to ask it, the proposal waits for input, and a later request of the same tool in that conversation is handed that waiting proposal

#### Scenario: A flow tells the model why not
- **WHEN** a flow decides the proposal will not be put to a person
- **THEN** no person is asked, nothing is written, and the model is told the flow's reason

#### Scenario: No flow for the tool
- **WHEN** a write tool asks for input and no installed plugin contributes a flow for its name
- **THEN** no person is asked, nothing is written, and the model is told the write cannot be confirmed

### Requirement: One store keeps every waiting write
Every proposal SHALL be kept in one pending-writes store until it is resolved. A proposal is resolved when it is put to
a person and answered, or when it waits for input. The store holds who the proposal was put to, their tenant, the
conversation and turn, the tool's name, the opaque state, the summary, the question, the expiry, its status and the
flow's own data. A status SHALL change only by one guarded update from the status it is expected to have, so that two
replicas taking answers at once resolve it once. When a proposal resolves, the other proposals of the same tool still
open in that conversation SHALL be resolved with it.

#### Scenario: Answered on another replica
- **WHEN** a proposal made through one replica is answered through another
- **THEN** the answer is taken, and the proposal is resolved once

#### Scenario: Two answers at once
- **WHEN** two answers to the same proposal arrive at the same time
- **THEN** exactly one resolves it and the other reports it as no longer waiting

#### Scenario: Earlier proposals of the same tool
- **WHEN** a proposal is applied while an earlier proposal of the same tool in that conversation still waits for input
- **THEN** the earlier one is no longer open

#### Scenario: Proposals made before this store
- **WHEN** the service starts on a database holding proposals stored before this change
- **THEN** those proposals are still found, with their status, summary, question and expiry, and a waiting one is still answerable

### Requirement: The state never leaves the server
The opaque state SHALL be kept by the server and handed back only to the write tool. It SHALL NOT be sent to the
browser, in the run's pause or in any answer. An answer SHALL name the proposal by its id, and the server SHALL use the
state it keeps for that id.

#### Scenario: The pause carries no state
- **WHEN** a run pauses for a proposal
- **THEN** the pause carries the proposal's id, tool, summary, schema, question and expiry, and not its state

#### Scenario: Answering by id
- **WHEN** a person confirms a proposal by its id
- **THEN** the write tool is called with the state the server kept for that id

### Requirement: A person confirms, rejects, or lets it expire
Only the person a proposal was put to SHALL be able to answer it. Confirming SHALL call the write tool with the
proposal's state as the confirmation, and the write that executes SHALL be the one the state describes. Rejecting
SHALL write nothing and tell the agent the person declined. A proposal past its expiry SHALL NOT be answerable. When an
answer or a rejoin finds it so, it SHALL be recorded as expired. Each outcome SHALL be handed to the tool's flow so it
can record its own step.

#### Scenario: Confirmed
- **WHEN** the person confirms a waiting proposal
- **THEN** the write tool is called with that proposal's state, the write it describes happens, and the proposal is recorded as applied

#### Scenario: Rejected
- **WHEN** the person rejects a waiting proposal
- **THEN** nothing is written, the proposal is recorded as declined, and the agent is told the person declined

#### Scenario: Answered too late
- **WHEN** an answer arrives after the proposal's expiry
- **THEN** nothing is written, the proposal is recorded as expired, and the answer reports that it must be proposed again

#### Scenario: Rejoined too late
- **WHEN** a page rejoins a run whose proposal has expired
- **THEN** the proposal is recorded as expired and no pause is replayed

#### Scenario: Someone else answers
- **WHEN** a user other than the one the proposal was put to answers it
- **THEN** the answer is refused and nothing is written

### Requirement: A waiting write is found again
A person SHALL be able to ask what a conversation of theirs is waiting on. The answer SHALL be the waiting proposal:
its id, the tool's name, the summary, the schema its tool's flow gives it, the question and the expiry. When no
proposal is waiting it SHALL be nothing. Asking SHALL change nothing. A tool whose flow is no longer installed SHALL
be shown without a schema. Rejoining a run that paused for a proposal SHALL replay that proposal as the run's pause. A
conversation that is not theirs SHALL be reported as not found.

#### Scenario: Asking what is waiting
- **WHEN** a conversation has a proposal awaiting its answer
- **THEN** asking returns that proposal with its tool, summary, schema, question and expiry

#### Scenario: Rejoining a paused run
- **WHEN** a page rejoins a run that paused for a proposal that has not expired
- **THEN** the run's pause is replayed with the same proposal, still answerable

#### Scenario: Nothing waiting
- **WHEN** the proposal was applied, declined or has expired
- **THEN** asking returns nothing

#### Scenario: Another person's conversation
- **WHEN** someone who is not the owner asks
- **THEN** the conversation is reported as not found

### Requirement: The record of a write names no content
Every step of a write SHALL be recorded in the audit chain in identifiers: proposed, asked, answered, applied, declined,
expired and refused. Each record names the acting person, their tenant, the tool, the proposal and the outcome. A
summary, a reason, a person's typed input or a reviewer's text MUST NOT appear in any record or log.

#### Scenario: A confirmed write leaves a trail
- **WHEN** a proposal is made, confirmed and applied
- **THEN** a record exists for each step naming the person, the tool and the proposal, and the chain still verifies

#### Scenario: No content in the trail
- **WHEN** a proposal's summary and a person's typed input are given
- **THEN** neither appears in any record or log
