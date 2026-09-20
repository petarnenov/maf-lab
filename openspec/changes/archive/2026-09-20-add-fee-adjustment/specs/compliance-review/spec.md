# Spec Delta

## MODIFIED Requirements

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
