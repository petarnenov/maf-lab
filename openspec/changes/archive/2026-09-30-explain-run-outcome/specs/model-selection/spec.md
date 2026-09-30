## MODIFIED Requirements

### Requirement: Cost estimate before start
Before a run starts, the picker SHALL show an estimated cost for the selected model: the tokens expected for up to 5
attempts on this file, priced at the model's configured rates. It SHALL also say what limits the run: the budget the
administrator entered, or, when none is entered, that the run is unlimited and stops after at most 5 attempts.
Prices that are configured estimates SHALL be labelled as estimates.

#### Scenario: Estimate changes with the model
- **WHEN** the administrator switches from `glm-5.3:cloud` to `glm-5.3-flash:cloud`
- **THEN** the estimated cost updates to the flash model's prices

#### Scenario: Unlimited is said
- **WHEN** the administrator has entered no budget
- **THEN** the picker says the budget is unlimited and the run stops after at most 5 attempts

#### Scenario: Entered budget is said
- **WHEN** the administrator enters a cost cap of $0.50
- **THEN** the picker says the run stops at $0.50, and warns when the estimate is above it

## ADDED Requirements

### Requirement: Optional run budget
Next to the model, the picker SHALL offer two optional fields: max tokens (a whole number) and max cost in USD. Both
SHALL start empty, which means unlimited. A field left empty SHALL leave that dimension unlimited. A value that is not
positive SHALL be shown as invalid, and the Start control SHALL stay disabled until it is corrected or cleared. The
chosen budget SHALL be sent with the start request, and the server SHALL validate it again.

#### Scenario: Default is unlimited
- **WHEN** the picker opens
- **THEN** both budget fields are empty and the run would start without caps

#### Scenario: Only a cost cap
- **WHEN** the administrator enters $1.00 and leaves max tokens empty
- **THEN** the start request carries a cost cap of $1.00 and no token cap

#### Scenario: Invalid value
- **WHEN** the administrator enters 0 as max tokens
- **THEN** the field is marked invalid and Start is disabled
