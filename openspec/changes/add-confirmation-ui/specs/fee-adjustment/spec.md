# Spec Delta

## ADDED Requirements

### Requirement: What a conversation is waiting on can be asked for
A person SHALL be able to ask what a conversation of theirs is waiting on, and receive the proposal still
awaiting their answer — the same account, amount, resulting fee, period, question and expiry the run carried —
or nothing when none is. A conversation that is not theirs SHALL be reported as not found, and a proposal that
has been answered, refused or has expired SHALL NOT be returned as waiting.

#### Scenario: A proposal is waiting
- **WHEN** a conversation has a proposal awaiting its answer
- **THEN** asking returns that proposal with the account, the amount, the resulting fee, the period, the question and the expiry

#### Scenario: Nothing is waiting
- **WHEN** a conversation has no proposal awaiting an answer
- **THEN** asking returns nothing

#### Scenario: Already answered
- **WHEN** the proposal was applied or declined
- **THEN** asking returns nothing

#### Scenario: Another person's conversation
- **WHEN** someone who is not the owner asks
- **THEN** the conversation is reported as not found
