# Spec Delta

## MODIFIED Requirements

### Requirement: Only a person confirms
An adjustment SHALL be applied only after the user has approved that specific proposal. The model's output SHALL
never stand as the approval. When a proposal is waiting, the run SHALL pause carrying the proposal as the thing
it is waiting on — what the person must check, the shape of the answer and when the proposal stops being
answerable; approving SHALL apply it and rejecting SHALL apply nothing and tell the agent the user declined, so
the conversation continues.

#### Scenario: Waiting for a person
- **WHEN** a proposal needs confirming
- **THEN** the run pauses carrying the summary and what identifies the proposal, and no write has happened

#### Scenario: Approval applies it
- **WHEN** the user approves that proposal
- **THEN** the adjustment is applied and the account's current fee reflects it

#### Scenario: Rejection applies nothing
- **WHEN** the user rejects that proposal
- **THEN** nothing is applied, the agent is told the user declined, and it answers without proposing the same adjustment again in that turn

#### Scenario: Someone else's proposal
- **WHEN** a confirmation arrives from a user other than the one the proposal was issued to
- **THEN** it is refused and nothing is applied
