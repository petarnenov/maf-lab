# Spec Delta

## Purpose

A reviewer outside this system that says whether a proposed fee adjustment may proceed: one skill, a verdict with
a reason, sometimes a question first — and never an actual decision about anybody's money.

## ADDED Requirements

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
reason, the adjustment it concerns, and a marker that the review was simulated. Prose alone MUST NOT be the
result, because the caller is a program.

#### Scenario: Approved
- **WHEN** a review of a modest adjustment completes
- **THEN** the result carries `approved`, a reason, the adjustment's identifier and `simulated: true`

#### Scenario: Refused
- **WHEN** an adjustment breaches the reviewer's threshold
- **THEN** the result carries `refused` with a reason naming the threshold

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
