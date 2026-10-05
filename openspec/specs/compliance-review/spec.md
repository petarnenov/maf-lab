# compliance-review Specification

## Purpose

A reviewer outside this system that says whether a proposed fee adjustment may proceed: one skill, a verdict with
a reason, sometimes a question first — and never an actual decision about anybody's money.

## Requirements

### Requirement: One skill, stated plainly
The compliance reviewer SHALL be a separate service, discoverable by its own agent card, offering exactly one
skill: reviewing a proposed fee adjustment and returning a verdict. The skill's description SHALL state what it is
for and what it is not for, and SHALL say that the review is simulated and binds nobody.

#### Scenario: Discovery
- **WHEN** the reviewer's card is fetched
- **THEN** it names the agent and lists exactly one skill, whose description says both what it reviews and that
  its verdict is simulated

#### Scenario: Not a general assistant
- **WHEN** the reviewer is asked something unrelated to a fee adjustment
- **THEN** it answers that this is not what it is for, and returns no verdict

### Requirement: A verdict is structured, not prose
A completed review SHALL end with a structured result carrying: the decision (approved or refused), a short
reason, the adjustment it concerns, the account it concerns, and a marker that the review was simulated. Prose
alone MUST NOT be the result, because the caller is a program. The adjustment and the account SHALL be the ones
the review was asked about, so that a caller can tell whether the answer is about its question.

#### Scenario: Approved
- **WHEN** a review of a modest adjustment completes
- **THEN** the result carries `approved`, a reason, the adjustment's identifier, the account's identifier and `simulated: true`

#### Scenario: Refused
- **WHEN** an adjustment breaches the reviewer's threshold
- **THEN** the result carries `refused` with a reason naming the threshold

#### Scenario: The verdict answers the question it was asked
- **WHEN** a review of account A-1042 completes
- **THEN** the account in the result is A-1042

### Requirement: The reviewer takes its time
A review SHALL report progress while it works and SHALL take long enough that a caller must treat it as work in
progress rather than a function call. Its duration SHALL be configurable so tests and the stack can differ.

#### Scenario: Progress while working
- **WHEN** a review is running
- **THEN** the caller receives progress updates before any result

#### Scenario: Configured duration
- **WHEN** the configured review duration is short
- **THEN** the review completes accordingly, with the same states in the same order

### Requirement: The reviewer may ask before it answers
A review MAY stop and ask for the advisor's justification instead of answering. It SHALL then wait for that
justification under the same review, and SHALL reach a verdict once it is supplied. How often this happens SHALL
be configurable, so a test can make it certain and the stack can make it occasional.

#### Scenario: A question instead of a verdict
- **WHEN** the reviewer is configured to always ask
- **THEN** the review stops in the state that asks for input, carrying a question about the justification

#### Scenario: Supplying the justification
- **WHEN** the justification is sent for that review
- **THEN** the same review resumes and completes with a verdict

#### Scenario: Never asked twice
- **WHEN** a review has already asked and been answered
- **THEN** it does not ask again for the same review

### Requirement: The reviewer authenticates its callers
The reviewer SHALL accept only callers presenting a valid token for its own audience, obtained with client
credentials it recognises. An unauthenticated request SHALL be refused, and the reviewer MUST NOT infer any user
identity from a caller.

#### Scenario: Anonymous request
- **WHEN** a review is requested without a token
- **THEN** it is refused

#### Scenario: A token for another audience
- **WHEN** a token issued for the assistant's own A2A surface is presented to the reviewer
- **THEN** it is refused

### Requirement: The reviewer's work survives the replica that took it
The reviewer's tasks and webhook registrations SHALL be kept outside the serving process, so that any replica can
report a task's state and deliver the changes a caller registered for. A task started through one replica SHALL be
readable through another, and a webhook registered through one SHALL be honoured whichever replica sees the state
change.

#### Scenario: A verdict asked for through another replica
- **WHEN** a review is started through one reviewer replica and its task is fetched through another
- **THEN** the second replica reports the same task with the same state

#### Scenario: A webhook registered on one replica
- **WHEN** a webhook is registered through one reviewer replica and the task changes state on another
- **THEN** the delivery is made

### Requirement: The threshold is a size, not a direction
The reviewer SHALL refuse an adjustment whose size exceeds its refusal threshold, whether the adjustment raises the
fee or reduces it. An adjustment whose size is at or below the threshold SHALL NOT be refused for its size.

#### Scenario: A large credit
- **WHEN** the threshold is 1,000 and an adjustment of -4,116 is reviewed
- **THEN** the result carries `refused` with a reason naming the threshold

#### Scenario: A large increase
- **WHEN** the threshold is 1,000 and an adjustment of +4,116 is reviewed
- **THEN** the result carries `refused` with a reason naming the threshold

#### Scenario: A credit at the threshold
- **WHEN** the threshold is 1,000 and an adjustment of -1,000 is reviewed
- **THEN** the result is not refused for its size

### Requirement: A cancel stops the review wherever it runs
A `tasks/cancel` received by any reviewer replica SHALL stop the review, whichever replica is running it. The shared
task store SHALL be the one place a cancel is known: no replica SHALL need to be the one running the review to cancel
it, and no routing to a particular replica SHALL be required.
- Once a task is in a terminal state (`completed`, `canceled`, `failed`, `rejected`), the store SHALL NOT let any
  replica write a different state over it; the check and the write SHALL be one atomic operation in the store.
- The replica running a review SHALL notice a cancel recorded in the store within a short interval and stop: no further
  stage is started, no artifact is added, and the review does not complete.

#### Scenario: Cancelled through the other replica
- **WHEN** a review runs on one reviewer replica and `tasks/cancel` for its task is received by another
- **THEN** the task ends `canceled`, the running review stops without starting another stage, and the task is still
  `canceled` after the time the review would have taken

#### Scenario: A late write does not undo a cancel
- **WHEN** a task is `canceled` in the store and a replica then saves the same task as `working` or `completed`
- **THEN** the store keeps it `canceled`

#### Scenario: Finishing is not blocked
- **WHEN** a review completes without being cancelled
- **THEN** it is saved `completed` with its verdict, as before
