# Spec Delta

## Purpose

Lets the assistant consult another agent over A2A and keep its own promises while doing so: the remote agent is
discovered from its card, authenticated to as this system, and allowed to be slow, absent, or to ask a question
back without any of that reaching the caller as a hang or as a made-up answer.

## ADDED Requirements

### Requirement: A remote agent is found by its card
The system SHALL locate a sub-agent by fetching its agent card from its well-known location and using what the
card says — its name, its skills and where it answers. A remote endpoint MUST NOT be assembled from a hard-coded
path, so that moving the agent is a configuration change and nothing more.

Configuration SHALL name the agent's base address; everything else about it SHALL come from the card.

#### Scenario: Discovery before the first call
- **WHEN** the assistant consults the compliance agent for the first time
- **THEN** it has fetched that agent's card and calls the endpoint the card advertises

#### Scenario: No card, no call
- **WHEN** the card cannot be fetched
- **THEN** the consultation fails with a reason naming discovery, and no request is sent to a guessed address

### Requirement: The assistant calls as itself
The assistant SHALL authenticate to a sub-agent with its own service credentials. A user's token MUST NOT be
forwarded to a sub-agent, and nothing identifying the user MUST be sent as the caller's identity.

#### Scenario: The remote agent sees a system
- **WHEN** the assistant consults the compliance agent on behalf of a signed-in user
- **THEN** the compliance agent authenticates the assistant as a system, and the user's token appears nowhere in
  the request

#### Scenario: Without credentials, nothing is attempted
- **WHEN** the assistant has no credentials for a sub-agent
- **THEN** the consultation fails with that reason rather than being attempted anonymously

### Requirement: A slow or absent agent does not become a hang
A consultation SHALL have a deadline. When the deadline passes, when the remote agent cannot be reached, or when
it fails, the consultation SHALL end with an outcome that says which of those happened. The caller MUST NOT be
left waiting indefinitely, and a failure MUST NOT be reported as a verdict.

#### Scenario: The agent is down
- **WHEN** the compliance agent is not running and the assistant consults it
- **THEN** the consultation ends promptly reporting that the agent could not be reached, and no verdict is claimed

#### Scenario: The review outlives the deadline
- **WHEN** a review takes longer than the configured deadline
- **THEN** the consultation ends reporting the timeout, and the review's task id is kept so the answer can still
  be collected later

#### Scenario: The remote agent fails
- **WHEN** the remote agent answers with an error
- **THEN** the consultation reports the failure, and the error text is not presented as the reviewer's opinion

### Requirement: A question back is an answer
When a sub-agent needs something before it can answer, the consultation SHALL end reporting that a question was
asked, carrying the question and the task it belongs to. The system MUST NOT invent the missing information, and
MUST NOT treat the question as a refusal.

#### Scenario: The reviewer asks for a justification
- **WHEN** the compliance agent stops and asks for the advisor's justification
- **THEN** the consultation reports the question and the task id, and no verdict is claimed

#### Scenario: Answering continues the same review
- **WHEN** the missing information is supplied for that task
- **THEN** the same review continues and reaches a verdict, without starting a new one

### Requirement: Consultations are audited
Every consultation of a sub-agent SHALL be recorded with the agent consulted, the operation, the task it
concerned, the outcome and how long it took. The content of what was sent or received MUST NOT be recorded.

#### Scenario: A completed review is recorded
- **WHEN** a review returns a verdict
- **THEN** a record exists naming the agent, the task id, the outcome and the duration

#### Scenario: A failed consultation is recorded
- **WHEN** a consultation times out or the agent is unreachable
- **THEN** the attempt is recorded with that outcome

#### Scenario: No content in the record
- **WHEN** a review is sent with an adjustment's details
- **THEN** none of that text appears in any record
