## ADDED Requirements

### Requirement: A write tool asks before it writes
A tool that writes SHALL, when called without a confirmation, return the protocol's request for input (MCP
elicitation) and SHALL NOT write. The request SHALL carry a summary fit for a person to check, the JSON Schema that
describes that summary, an opaque state that identifies the proposal, and when the proposal stops being answerable.
The core SHALL read none of the summary's fields: it keeps the summary, shows it and hands it back.

#### Scenario: The first call writes nothing
- **WHEN** a write tool is called without a confirmation
- **THEN** the result is a request for input carrying the summary, its schema, the state and the expiry, and nothing is written

#### Scenario: A summary the core has never seen
- **WHEN** a plugin's write tool sends a summary with fields no other tool uses
- **THEN** the proposal is kept and shown with those fields, and the core needed no change to do so

### Requirement: Each write tool has its own flow
Between a tool's request for input and the person, the core SHALL hand the proposal to the write-confirmation flow
contributed for that tool's name. The flow SHALL decide whether the person is asked now, asked for more input first, or
the model is told why not. A write tool with no flow SHALL be refused: the model is told the write cannot be
confirmed, no person is asked, and nothing is written.

#### Scenario: A flow asks the person
- **WHEN** a write tool asks for input and its flow decides the person should confirm
- **THEN** the run pauses with the flow's question and summary, and no write has happened

#### Scenario: A flow tells the model why not
- **WHEN** a flow decides the proposal will not be put to a person
- **THEN** no person is asked, nothing is written, and the model is told the flow's reason

#### Scenario: No flow for the tool
- **WHEN** a write tool asks for input and no installed plugin contributes a flow for its name
- **THEN** no person is asked, nothing is written, and the model is told the write cannot be confirmed

### Requirement: One store keeps every waiting write
Every proposal put to a person SHALL be kept in one pending-writes store until it is resolved. It holds who it was put
to, their tenant, the conversation and turn, the tool's name, the opaque state, the summary, the question, the expiry,
its status and the flow's own data. A status SHALL change only by one guarded update from the status it is expected
to have, so that two replicas taking answers at once resolve it once.

#### Scenario: Answered on another replica
- **WHEN** a proposal made through one replica is answered through another
- **THEN** the answer is taken, and the proposal is resolved once

#### Scenario: Two answers at once
- **WHEN** two answers to the same proposal arrive at the same time
- **THEN** exactly one resolves it and the other reports it as no longer waiting

#### Scenario: Proposals made before this store
- **WHEN** the service starts on a database holding proposals stored before this change
- **THEN** those proposals are still found, with their status, summary, question and expiry, and a waiting one is still answerable

### Requirement: A person confirms, rejects, or lets it expire
Only the person a proposal was put to SHALL be able to answer it. Confirming SHALL call the write tool with the
proposal's state as the confirmation, and the write that executes SHALL be the one the state describes. Rejecting
SHALL write nothing and tell the agent the person declined. A proposal past its expiry SHALL NOT be answerable and SHALL
be recorded as expired. Each outcome SHALL be handed to the tool's flow so it can record its own step.

#### Scenario: Confirmed
- **WHEN** the person confirms a waiting proposal
- **THEN** the write tool is called with that proposal's state, the write it describes happens, and the proposal is recorded as applied

#### Scenario: Rejected
- **WHEN** the person rejects a waiting proposal
- **THEN** nothing is written, the proposal is recorded as declined, and the agent is told the person declined

#### Scenario: Expired
- **WHEN** an answer arrives after the proposal's expiry
- **THEN** nothing is written, the proposal is recorded as expired, and the answer reports that it must be proposed again

#### Scenario: Someone else answers
- **WHEN** a user other than the one the proposal was put to answers it
- **THEN** the answer is refused and nothing is written

### Requirement: A waiting write is found again
A person SHALL be able to ask what a conversation of theirs is waiting on and receive the waiting proposal: its id, the
tool's name, the summary and its schema, the question and the expiry. They receive nothing when no proposal is
waiting. Rejoining a run that paused for a proposal SHALL replay that proposal as the run's pause. A conversation that
is not theirs SHALL be reported as not found.

#### Scenario: Asking what is waiting
- **WHEN** a conversation has a proposal awaiting its answer
- **THEN** asking returns that proposal with its tool, summary, schema, question and expiry

#### Scenario: Rejoining a paused run
- **WHEN** a page rejoins a run that paused for a proposal
- **THEN** the run's pause is replayed with the same proposal, still answerable

#### Scenario: Nothing waiting
- **WHEN** the proposal was applied, declined or has expired
- **THEN** asking returns nothing

#### Scenario: Another person's conversation
- **WHEN** someone who is not the owner asks
- **THEN** the conversation is reported as not found

### Requirement: The record of a write names no content
Every step of a write (proposed, asked, answered, applied, declined, expired, refused) SHALL be recorded in the audit
chain in identifiers: the acting person, their tenant, the tool, the proposal and the outcome. A summary, a reason, a
person's typed input or a reviewer's text MUST NOT appear in any record or log.

#### Scenario: A confirmed write leaves a trail
- **WHEN** a proposal is made, confirmed and applied
- **THEN** a record exists for each step naming the person, the tool and the proposal, and the chain still verifies

#### Scenario: No content in the trail
- **WHEN** a proposal's summary and a person's typed input are given
- **THEN** neither appears in any record or log
