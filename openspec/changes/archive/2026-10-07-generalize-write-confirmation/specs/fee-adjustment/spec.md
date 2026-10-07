## ADDED Requirements

### Requirement: The fee adjustment is one write-confirmation flow
Billing SHALL implement the core's write-confirmation seam for `propose_fee_adjustment`: its flow decides whether
the reviewer must see a proposal first, asks for a justification when the reviewer wants one, and records the
`fee.adjustment.*` steps, reaching the reviewer, the audit chain, the screening of the reviewer's words and the
turn's trace only through the core's ports. The rules of this specification — the review threshold, at most two questions,
the verdict check, applied at most once, never below zero — SHALL hold exactly as before. The summary it puts to a
person SHALL carry its fields under a schema whose titles name them.

#### Scenario: The same rules through the seam
- **WHEN** an adjustment above the threshold is proposed, reviewed, confirmed and applied through the seam
- **THEN** the reviewer was consulted first, the person was asked once, the fee moved once, and every `fee.adjustment.*` step was recorded

#### Scenario: A justification is input the flow asked for
- **WHEN** the reviewer asks for the advisor's justification
- **THEN** the model is told to ask it, the proposal waits for input, the advisor's answer reaches the flow as the next proposal of the same adjustment, and the same review continues

## MODIFIED Requirements

### Requirement: What a conversation is waiting on can be asked for
A person SHALL be able to ask what a conversation of theirs is waiting on, as write-confirmation provides for every
write. For a fee adjustment the proposal returned SHALL carry, in its summary, the same account, amount, resulting fee
and period the run carried, with the question and the expiry, or nothing when none is waiting. A conversation that is
not theirs SHALL be reported as not found, and a proposal that has been answered, refused or has expired SHALL NOT be
returned as waiting.

#### Scenario: A proposal is waiting
- **WHEN** a conversation has a fee adjustment awaiting its answer
- **THEN** asking returns that proposal with the account, the amount, the resulting fee and the period in its summary, and the question and the expiry

#### Scenario: Nothing is waiting
- **WHEN** a conversation has no proposal awaiting an answer
- **THEN** asking returns nothing

#### Scenario: Already answered
- **WHEN** the proposal was applied or declined
- **THEN** asking returns nothing

#### Scenario: Another person's conversation
- **WHEN** someone who is not the owner asks
- **THEN** the conversation is reported as not found
