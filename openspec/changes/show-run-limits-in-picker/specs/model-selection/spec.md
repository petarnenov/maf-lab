# Spec Delta

## MODIFIED Requirements

### Requirement: Cost estimate before start
Before a run starts, the picker SHALL show an estimated cost for the selected model, for the limits currently
entered: the attempts entered, each attempt priced as a tool loop whose input grows with the tool rounds it may take
(counted up to a typical number of rounds per attempt) and with the target file's size, plus the output an attempt
typically writes, at the model's configured rates. The estimate's constants SHALL be fitted to measured runs and SHALL
be recorded with the runs they came from. The estimate SHALL update when the model, the attempts or the tool rounds
change. It SHALL also say what limits the run: the budget the administrator entered, or, when the caps are unlimited,
that the run is unlimited and stops after at most the attempts entered. Prices that are configured estimates SHALL be
labelled as estimates.

#### Scenario: Estimate changes with the model
- **WHEN** the administrator switches from `glm-5.3:cloud` to `glm-5.3-flash:cloud`
- **THEN** the estimated cost updates to the flash model's prices

#### Scenario: Estimate follows the attempts entered
- **WHEN** the administrator lowers max attempts from 10 to 4
- **THEN** the estimated cost is four tenths of what it was for 10 attempts

#### Scenario: Estimate follows fewer tool rounds
- **WHEN** the administrator lowers the tool rounds per attempt below the typical number of rounds
- **THEN** the estimated cost falls

#### Scenario: Estimate is in line with measured runs
- **WHEN** the picker estimates one attempt with `glm-5.3:cloud` on a 3.7 KB file with the default limits
- **THEN** the per-attempt estimate is between $0.06 and $0.12, the range measured runs on such files cost

#### Scenario: Unlimited is said
- **WHEN** both caps are left unlimited
- **THEN** the picker says the budget is unlimited and the run stops after at most the attempts entered

#### Scenario: Entered budget is said
- **WHEN** the administrator enters a cost cap of $0.50
- **THEN** the picker says the run stops at $0.50, and warns when the estimate is above it

### Requirement: Optional run budget
Next to the model, the picker SHALL offer two optional caps: max tokens (a whole number) and max cost in USD. Each
SHALL show its default, unlimited, as a checked "Unlimited" control. Unchecking it SHALL enable the field and fill it
with the selected model's estimate for that dimension, which the administrator may edit. A cap left unlimited SHALL
leave that dimension unlimited. A value that is not positive SHALL be shown as invalid, and the Start control SHALL
stay disabled until it is corrected or the cap is set back to unlimited. The chosen budget SHALL be sent with the
start request, and the server SHALL validate it again.

#### Scenario: Default is unlimited
- **WHEN** the picker opens
- **THEN** both caps show "Unlimited" checked and the run would start without caps

#### Scenario: Setting a cap starts from the estimate
- **WHEN** the administrator unchecks "Unlimited" for max cost while the estimate is $0.89
- **THEN** the max cost field is enabled and holds 0.89

#### Scenario: Only a cost cap
- **WHEN** the administrator sets a cost cap of $1.00 and leaves max tokens unlimited
- **THEN** the start request carries a cost cap of $1.00 and no token cap

#### Scenario: Invalid value
- **WHEN** the administrator enters 0 as max tokens
- **THEN** the field is marked invalid and Start is disabled

## ADDED Requirements

### Requirement: Every run limit shown with its default
The model step SHALL show every limit that bounds a run, each filled with its default, served by the api rather than
written into the browser. Editable, with their bounds: max attempts (default 10, from 1 to 10), tool rounds per attempt
(default 40, from 1 to 40) and test runs the model may start per attempt (default 2, from 0 to 2), next to the two
budget caps. Shown read-only: the target line coverage, the run deadline, and the number of suspected bugs a run may
report. A value outside its bounds, or not a whole number, SHALL be shown as invalid with its bounds, and the Start
control SHALL stay disabled until it is corrected. The limits entered SHALL be sent with the start request.

#### Scenario: Defaults shown
- **WHEN** the model step opens
- **THEN** it shows max attempts 10, tool rounds per attempt 40, test runs per attempt 2, both caps unlimited, the target, the deadline and the suspected-bug limit of 3

#### Scenario: Fewer attempts
- **WHEN** the administrator sets max attempts to 3 and starts the run
- **THEN** the start request carries max attempts 3, and the run reports attempt n of 3

#### Scenario: Above the bound
- **WHEN** the administrator enters 50 tool rounds per attempt
- **THEN** the field is marked invalid, says it must be from 1 to 40, and Start is disabled

#### Scenario: Limits unchanged
- **WHEN** the administrator starts without touching the limits
- **THEN** the run has the default limits
