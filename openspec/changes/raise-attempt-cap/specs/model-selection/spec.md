## MODIFIED Requirements

### Requirement: Cost estimate before start
Before a run starts, the picker SHALL show an estimated cost for the selected model: the tokens expected for up to
the attempt cap (10 attempts) on this file, priced at the model's configured rates. It SHALL also say what limits the run: the budget the
administrator entered, or, when none is entered, that the run is unlimited and stops after at most 10 attempts.
Prices that are configured estimates SHALL be labelled as estimates.

#### Scenario: Estimate changes with the model
- **WHEN** the administrator switches from `glm-5.3:cloud` to `glm-5.3-flash:cloud`
- **THEN** the estimated cost updates to the flash model's prices

#### Scenario: Unlimited is said
- **WHEN** the administrator has entered no budget
- **THEN** the picker says the budget is unlimited and the run stops after at most 10 attempts

#### Scenario: Entered budget is said
- **WHEN** the administrator enters a cost cap of $0.50
- **THEN** the picker says the run stops at $0.50, and warns when the estimate is above it
