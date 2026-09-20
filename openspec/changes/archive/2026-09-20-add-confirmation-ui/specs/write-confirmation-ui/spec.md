# Spec Delta

## Purpose
What a person sees when a write is waiting for their word: the proposal in the conversation where it was made,
the two answers they can give, and the same card still waiting when they come back to it tomorrow.

## ADDED Requirements

### Requirement: A waiting write is shown in the conversation
When a run pauses for an approval, the conversation SHALL show what is waiting, in place, as part of the turn
that proposed it — not as a dialog that covers the conversation. It SHALL show the account by id and name, the
fee as it stands, the amount, the fee that would result, the period affected, and the question in the words the
server asked it.

#### Scenario: A proposal appears
- **WHEN** a run pauses for an approval
- **THEN** the turn shows the account, the current fee, the amount, the resulting fee and the period, with a way to approve and a way to reject

#### Scenario: Not a dialog
- **WHEN** a proposal is waiting
- **THEN** the rest of the conversation is still readable and the history is still usable

#### Scenario: Nothing has happened yet
- **WHEN** a proposal is waiting and nobody has answered
- **THEN** nothing says the adjustment was applied

### Requirement: Approving and rejecting are the person's two answers
The card SHALL offer exactly two answers. Approving SHALL resume the proposal as approved and rejecting SHALL
resume it as declined; both SHALL report the outcome in the conversation, and the card SHALL then show what
became of the proposal rather than continuing to ask. While an answer is in flight neither answer SHALL be
accepted a second time.

#### Scenario: Approving
- **WHEN** the person approves
- **THEN** the proposal is resumed as approved, the conversation says what was applied, and the card no longer offers to answer

#### Scenario: Rejecting
- **WHEN** the person rejects
- **THEN** the proposal is resumed as declined, the conversation says nothing was applied, and the card no longer offers to answer

#### Scenario: One answer at a time
- **WHEN** an answer is in flight
- **THEN** the buttons cannot be pressed again

#### Scenario: A proposal that is no longer waiting
- **WHEN** the answer reports that the proposal is no longer waiting
- **THEN** the card says so and offers no further answer

### Requirement: A proposal outlives the page
A proposal that is still waiting SHALL be findable from the conversation it belongs to, so that opening that
conversation again shows the same card. What is shown SHALL be the proposal as the server has it, and only the
person it was put to SHALL be able to see or answer it.

#### Scenario: Coming back to it
- **WHEN** a conversation with a waiting proposal is opened again
- **THEN** the same proposal is shown, with the same account, amount and resulting fee, still answerable

#### Scenario: Answered in the meantime
- **WHEN** the proposal was answered elsewhere before the page was opened
- **THEN** no card is shown

#### Scenario: Somebody else's conversation
- **WHEN** another user asks what a conversation is waiting on
- **THEN** they are told there is no such conversation

### Requirement: A proposal says when it stops being answerable
The card SHALL say when the proposal expires. Once it has expired the card SHALL NOT offer to answer, and SHALL
say that the proposal has to be made again.

#### Scenario: Still answerable
- **WHEN** a proposal is waiting and has not expired
- **THEN** the card says when it expires and both answers are offered

#### Scenario: Expired
- **WHEN** the proposal's expiry has passed
- **THEN** no answer is offered and the card says it must be proposed again

### Requirement: A slow step says it is slow
A tool call that is expected to take tens of seconds SHALL say so while it runs, including roughly how long it
takes, rather than appearing to be stuck.

#### Scenario: A compliance review
- **WHEN** a turn is waiting for a compliance review
- **THEN** the card says a review is under way and roughly how long one takes
