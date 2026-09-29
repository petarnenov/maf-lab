# Spec Delta

## ADDED Requirements

### Requirement: Answers build on data cards instead of restating them
When a turn has shown a data card, the answer SHALL NOT repeat the card's data as a table. It SHALL NOT list the card's
rows one by one either, unless the question explicitly asks about every row (every asset class, every account).
Instead the answer SHALL say what the data means for the question, in the language the question was asked in.

When the answer quotes figures from a rebalance plan, they SHALL be the plan's figures. The assistant SHALL NOT compute
trades or weights of its own. The assistant SHALL NOT offer to prepare, place or confirm trades, which nothing in the system
executes.

When the plan says no rebalance is needed, the answer SHALL say so. It MAY still mention how far a class is from its
target.

The system prompt in use SHALL be selectable by configuration, so that the previous prompt can be restored without a
code change.

#### Scenario: A rebalance question within tolerance
- **WHEN** the user asks "Препоръчай ребалансиране за A-1043" and every class is within tolerance
- **THEN** the answer contains no markdown table, says that no rebalance is needed, and any amount it quotes appears in
  the plan

#### Scenario: A drifted account
- **WHEN** the user asks whether an account outside its tolerance needs rebalancing
- **THEN** the answer names the drifted class, says a rebalance is needed, and quotes the plan's trade for it

#### Scenario: A question about every class
- **WHEN** the user asks how far each asset class of an account is from its target
- **THEN** the answer may go through the classes one by one, and still has no markdown table

#### Scenario: Rolling back the prompt
- **WHEN** `Agent:SystemPrompt` is set to `system.v2`
- **THEN** the assistant runs with the previous prompt
