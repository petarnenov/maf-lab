# Spec Delta

## Purpose

Lets another agent discover, authenticate to and work with the billing assistant over A2A — asking questions,
starting work that takes time, following it, and being told plainly when it asks for something it may not have.

## ADDED Requirements

### Requirement: Discoverable agent card
The system SHALL publish an agent card at the well-known location, describing the agent's name, description,
version, the transports it accepts, the skills it offers and how to authenticate. Each skill SHALL state both what
it is for and what it is not for, so a caller can choose without guessing.

The card SHALL declare authentication as OAuth2 client credentials against the lab's issuer, with the scope a
partner needs. The card SHALL be signed, and the verification procedure SHALL be documented where a partner can
find it.

#### Scenario: Discovery
- **WHEN** an unauthenticated client fetches the well-known agent card
- **THEN** it receives the card with the agent's name, version, transports, skills and security schemes

#### Scenario: A skill says what it is not for
- **WHEN** a caller reads the documentation-search skill
- **THEN** it states both when to use it and what not to use it for

#### Scenario: The card is verifiable
- **WHEN** a partner verifies the card's signature by the documented procedure
- **THEN** the signature is valid for the card as served

### Requirement: Extended card after authentication
The system SHALL declare that it offers an extended card, and SHALL serve one only to an authenticated caller. The
extended card SHALL contain at least one skill that the public card does not, and the public card MUST NOT reveal
its existence beyond the declaration that an extended card exists.

#### Scenario: The public card hides the private skill
- **WHEN** the public card is fetched
- **THEN** it does not contain `start_billing_run` anywhere

#### Scenario: The extended card adds it
- **WHEN** an authenticated partner fetches the extended card
- **THEN** it contains `start_billing_run` in addition to the public skills

#### Scenario: No token, no extended card
- **WHEN** the extended card is requested without a valid token
- **THEN** the request is refused and no skill list is returned

### Requirement: The caller is a system, not a person
An inbound A2A caller SHALL be authenticated as a partner system: a token whose audience is this endpoint and whose
subject is the partner. It MUST NOT be treated as a user, and it MUST NOT inherit any user's entitlements.

What a partner may see SHALL be decided on the server from the firms that partner is entitled to, never from
anything in the request. A request concerning a firm outside that set SHALL be rejected with a short reason and
SHALL return no data about it — not its existence, not its status.

#### Scenario: A partner asks about a firm it is entitled to
- **WHEN** a partner entitled to firm A asks for the status of one of firm A's runs
- **THEN** it receives the status

#### Scenario: A partner asks about a firm it is not entitled to
- **WHEN** the same partner asks about a run of firm B
- **THEN** the task is rejected with a short reason, the answer contains nothing about firm B, and the attempt is recorded

#### Scenario: A user token is not a partner token
- **WHEN** a token issued for a chat user is presented to the A2A endpoint
- **THEN** the request is refused

### Requirement: Two shapes of work
A request the assistant can answer at once SHALL be answered with a message. A request that takes time SHALL be
answered with a task that the caller can follow: it SHALL report progress while it works, and SHALL end with a
structured artifact describing the result.

A task that cannot proceed without something the caller did not supply SHALL enter the state that asks for input,
SHALL say what is missing, and SHALL continue when the caller sends it under the same task. A task SHALL be
cancellable while it is working, and cancelling SHALL stop it.

#### Scenario: A question is answered directly
- **WHEN** a partner asks for the status of a known run
- **THEN** the answer is a message, not a task

#### Scenario: Work that takes time
- **WHEN** a partner asks to start a billing run
- **THEN** a task is created, its progress is reported while it works, and it completes with an artifact describing the final status

#### Scenario: Something is missing
- **WHEN** a partner asks to start a run without saying which period
- **THEN** the task asks for the period and says so, and supplying it under the same task lets the task finish

#### Scenario: Cancelled while working
- **WHEN** a partner cancels a working task
- **THEN** the task stops and reports that it was cancelled

### Requirement: A task survives the connection and the replica
Task and context state SHALL be kept outside the serving process, so that any replica can report a task's state and
history. A caller that loses its stream SHALL be able to resubscribe and SHALL receive the task's full current
state before any further updates, so that no transition is missed.

#### Scenario: Another replica answers
- **WHEN** a task is started through one replica and asked for through another
- **THEN** the second replica reports the same task with the same state

#### Scenario: A dropped stream loses nothing
- **WHEN** a caller's stream drops mid-task and it resubscribes
- **THEN** it first receives the task as it stands and then the remaining updates, with no transition missing

#### Scenario: The final artifact is retrievable afterwards
- **WHEN** a completed task is fetched
- **THEN** it carries the same artifact the stream delivered

### Requirement: Push notification of state changes
A caller SHALL be able to register a webhook for a task and SHALL then receive exactly one delivery per state
change, carrying the token it registered so the receiver can tell the call is genuine. A webhook that fails SHALL
NOT stop the task, and the failure SHALL be visible.

#### Scenario: One delivery per transition
- **WHEN** a caller registers a webhook and the task moves through working and completion
- **THEN** the receiver gets one delivery per transition, each carrying the registered token

#### Scenario: A failing webhook does not fail the task
- **WHEN** the registered webhook is unreachable
- **THEN** the task still reaches its final state and the delivery failure is recorded

### Requirement: Inbound requests are audited
Every inbound A2A request SHALL be recorded in the same audited record as the system's other actions, with the
partner's identity, the operation, the task it concerns, the outcome and the duration. The content of a message
MUST NOT be recorded.

#### Scenario: A request is recorded
- **WHEN** a partner starts a task
- **THEN** a record exists naming the partner, the operation, the task id, the outcome and the duration

#### Scenario: A refusal is recorded
- **WHEN** a partner's request is rejected as out of scope
- **THEN** the rejection is recorded with that outcome

#### Scenario: No message content
- **WHEN** a partner's message contains a question
- **THEN** that text appears in no audit record

### Requirement: The wire format is the specification's, not the SDK's
Requests and responses on the A2A surface SHALL use the A2A 1.0 wire format, whatever dialect the underlying SDK
speaks: the specified method names, the specified role and task-state values, parts that carry their `kind`, and
results that are the object itself rather than a wrapper around it. A client written against the specification
SHALL interoperate without knowing which library serves it.

Every divergence between the SDK and the specification SHALL be recorded where a reader can find it, together with
what is done about it.

#### Scenario: A specification-conformant request is understood
- **WHEN** a client sends `message/send` with `"role": "user"` and a part whose `kind` is `text`
- **THEN** the request is accepted and answered

#### Scenario: A specification-conformant response is returned
- **WHEN** a task is fetched
- **THEN** its state reads `completed`, its messages read `agent`, its parts carry their `kind`, and the result is the task itself

#### Scenario: Streamed events are specified events
- **WHEN** a caller streams a run
- **THEN** each event carries its `kind`, the last one is marked final, and no earlier one is

#### Scenario: The divergences are written down
- **WHEN** a reader opens the decision record
- **THEN** it names each place the preview SDK departs from 1.0 and what the service does about it
