# fee-adjustment Specification

## Purpose
Covers the first operation that changes something: proposing an adjustment to an account's fee, having it reviewed
when it is large enough to matter, putting it to the person who is accountable for it, and applying it exactly once.

## Requirements

### Requirement: Accounts and their current fee
Each firm SHALL have accounts, each with an identifier, a name and a fee. An account SHALL be visible only to its
own firm, and the firm SHALL be taken from the caller's token. An account's **current fee** SHALL be its seeded fee
adjusted by every adjustment that has been applied to it, in the order they were applied.

#### Scenario: Current fee reflects applied adjustments
- **WHEN** an account's seeded fee is 1,200 and an adjustment of -200 has been applied
- **THEN** its current fee reads 1,000

#### Scenario: Another firm's account
- **WHEN** a user of firm A names an account belonging to firm B
- **THEN** the answer is that no such account was found, and nothing reveals that it exists

### Requirement: A proposal writes nothing
`propose_fee_adjustment` SHALL accept an account, an amount and a reason, and SHALL be annotated as a tool that
writes: not read-only, destructive, not idempotent. Called without a confirmation, it SHALL validate the proposal
and return a request for input, never a change. The request SHALL carry a summary fit for a person to check — the
account and its name, the current fee, the amount, the fee that would result, and the period it would affect — and
an opaque state that identifies the proposal.

#### Scenario: Nothing is written on the first call
- **WHEN** the tool is called with an account, an amount and a reason
- **THEN** the result asks for input and carries the summary and the state, and the account's current fee is unchanged

#### Scenario: The tool declares what it is
- **WHEN** the tool list is inspected
- **THEN** `propose_fee_adjustment` is annotated read-only false, destructive true, idempotent false

#### Scenario: A proposal that cannot stand
- **WHEN** the account does not belong to the caller's firm, the amount is zero, or the reason is empty
- **THEN** the result is an error naming what is wrong, no input is requested, and nothing is written

### Requirement: The proposal that executes is the proposal that was confirmed
The state returned with a proposal SHALL be protected so that the system can tell whether it was altered after it
was issued. On a confirmed call the system SHALL execute what the state says, and SHALL ignore any account, amount
or reason supplied alongside it that disagrees. A state that has been altered, that was issued for another firm, or
that has expired SHALL be refused without writing.

#### Scenario: Altered state
- **WHEN** a confirmation arrives whose state has been changed after it was issued
- **THEN** the call is refused, nothing is written, and the refusal does not say how the protection works

#### Scenario: Arguments disagree with the state
- **WHEN** a confirmation carries a state for account A-1042 at -200 but arguments naming account A-2001 at -900
- **THEN** the adjustment applied is the one in the state, for A-1042 at -200

#### Scenario: Another firm's state
- **WHEN** a user of firm B confirms a state issued to a user of firm A
- **THEN** the call is refused and nothing is written

### Requirement: Large adjustments are reviewed before a person is asked
An adjustment whose amount exceeds a configured threshold SHALL be reviewed by the compliance agent before any
confirmation is put to the user. An adjustment at or below the threshold SHALL NOT be reviewed. A review SHALL be
requested at most twice for one proposal.

#### Scenario: Above the threshold
- **WHEN** an adjustment larger than the threshold is proposed
- **THEN** the reviewer is consulted and no confirmation is offered until it has answered

#### Scenario: At or below the threshold
- **WHEN** an adjustment within the threshold is proposed
- **THEN** no review is requested and the confirmation is put to the user directly

#### Scenario: The reviewer refuses
- **WHEN** the review comes back refused
- **THEN** the user is told it was refused and why, no confirmation is offered, and nothing is written

#### Scenario: The reviewer asks a question
- **WHEN** the review stops and asks for the advisor's justification
- **THEN** the user is asked that question, their answer continues the same review, and a verdict is reached without a second proposal

#### Scenario: The reviewer never answers
- **WHEN** the review times out, the reviewer cannot be reached, or it fails
- **THEN** the user is told which of those happened, no confirmation is offered, and nothing is written

#### Scenario: The question is not asked a third time
- **WHEN** a review asks for a justification twice for the same proposal
- **THEN** the flow ends without a confirmation rather than asking again

### Requirement: A verdict is checked before it is believed
A verdict SHALL be used only if it carries the fields it is required to carry and names the same adjustment and the
same account as the request it answers. A verdict that does not SHALL be treated as a failed review, not as an
approval or a refusal. The identifiers used afterwards SHALL be the ones this system sent, never the ones the
reviewer returned.

#### Scenario: A verdict about something else
- **WHEN** a verdict names a different account or a different adjustment than was sent
- **THEN** the review counts as failed, no confirmation is offered, and nothing is written

#### Scenario: A verdict missing its decision
- **WHEN** a verdict arrives without a decision
- **THEN** the review counts as failed rather than as a refusal

#### Scenario: Anything but approval is not approval
- **WHEN** a verdict's decision is a value other than approved
- **THEN** it is not treated as approval

### Requirement: Only a person confirms
An adjustment SHALL be applied only after the user has approved that specific proposal. The model's output SHALL
never stand as the approval. When a proposal is waiting, the turn SHALL end telling the client what is waiting and
carrying the state; approving SHALL apply it and rejecting SHALL apply nothing and tell the agent the user
declined, so the conversation continues.

#### Scenario: Waiting for a person
- **WHEN** a proposal needs confirming
- **THEN** the turn ends with a `confirmation_required` event carrying the summary and the state, and no write has happened

#### Scenario: Approval applies it
- **WHEN** the user approves that proposal
- **THEN** the adjustment is applied and the account's current fee reflects it

#### Scenario: Rejection applies nothing
- **WHEN** the user rejects that proposal
- **THEN** nothing is applied, the agent is told the user declined, and it answers without proposing the same adjustment again in that turn

#### Scenario: Someone else's proposal
- **WHEN** a confirmation arrives from a user other than the one the proposal was issued to
- **THEN** it is refused and nothing is applied

### Requirement: Applied at most once
Applying an adjustment SHALL be durable and SHALL survive a restart. The same proposal SHALL apply at most once,
however many times its confirmation arrives and whichever replica receives it. A repeated confirmation SHALL report
the adjustment as already applied, with the same identifier and the same resulting fee as the first.

#### Scenario: The same confirmation twice
- **WHEN** the same confirmed proposal is submitted twice
- **THEN** the fee moves once, the second answer says it was already applied, and both answers name the same adjustment

#### Scenario: Two replicas at once
- **WHEN** the same confirmed proposal reaches two replicas simultaneously
- **THEN** exactly one applies it and the other reports it as already applied

#### Scenario: Applied adjustments outlive the process
- **WHEN** the service restarts after an adjustment was applied
- **THEN** the account's current fee still reflects it

### Requirement: Every step of a write is recorded
Proposing, reviewing, confirming, rejecting and applying an adjustment SHALL each be recorded in the audit chain,
naming the person who acted, their firm, the account and the adjustment in identifiers, and the outcome. A refused
or failed step SHALL be recorded with that outcome. The reason, the justification, the verdict's text and the
confirmation summary MUST NOT appear in any record.

#### Scenario: The whole flow leaves a trail
- **WHEN** an adjustment is proposed, reviewed, confirmed and applied
- **THEN** a record exists for each step, each naming the acting person and the adjustment, and the chain still verifies

#### Scenario: Who approved it
- **WHEN** an applied adjustment is investigated
- **THEN** the record of the confirmation names the user who approved it

#### Scenario: No text in the trail
- **WHEN** a proposal's reason and a reviewer's verdict text are given
- **THEN** neither appears in any record
