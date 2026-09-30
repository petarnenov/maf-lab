# model-selection Specification

## Purpose
Defines which remote models the test-generation agent may use, how one is chosen for a run, and what the run is
expected to cost before it starts.

## Requirements

### Requirement: Configured allowlist
The models offered for a test-generation run SHALL come only from a backend allowlist in configuration. Each entry
SHALL carry a display name, the provider tag, prices per 1M input and output tokens, a short "best for" note, and
whether it is the default. Exactly one entry SHALL be the default. The initial allowlist is `glm-5.3:cloud`
(default, recommended), `kimi-k3:cloud`, `glm-5.3-flash:cloud`, `deepseek-v4-pro:cloud` and
`deepseek-v4.1-flash:cloud`. This allowlist SHALL NOT affect the model used by the chat assistant.

#### Scenario: Listing models
- **WHEN** an administrator opens the model picker
- **THEN** it lists each allowlisted model with name, prices and note, with the default preselected

#### Scenario: Chat model untouched
- **WHEN** the allowlist is changed
- **THEN** the chat assistant still uses its own configured model

### Requirement: Availability is shown, not assumed
The picker SHALL show whether each model is currently available to the configured account. A model the provider
has refused (for example, not included in the account's plan) SHALL be shown as unavailable and SHALL NOT be
selectable. Availability SHALL be checked without sending repository content, and SHALL be reused for a short,
stated period.

#### Scenario: Model not in the plan
- **WHEN** the provider answers that `kimi-k3:cloud` is not included in the account's plan
- **THEN** it is listed as unavailable and cannot be picked

### Requirement: Exactly one model, validated server-side
A run SHALL NOT start without exactly one selected model. The server SHALL validate the model against the allowlist
and its availability at start, whatever the browser sent. The agent SHALL receive only a model that passed this
validation.

#### Scenario: Model not in allowlist
- **WHEN** a start request names `llama9:cloud`, which is not in the allowlist
- **THEN** it is rejected as invalid, no run is created and the threshold is unchanged

#### Scenario: No model selected
- **WHEN** the administrator has not selected a model
- **THEN** the Start control is disabled, and a request without a model is rejected by the server

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
with the selected model's estimate for that dimension (a cost rounded up to the cent, so the filled cap is not below the estimate), which the administrator may edit. A cap left unlimited SHALL
leave that dimension unlimited. A value that is not positive SHALL be shown as invalid, and the Start control SHALL
stay disabled until it is corrected or the cap is set back to unlimited. The chosen budget SHALL be sent with the
start request, and the server SHALL validate it again.

#### Scenario: Default is unlimited
- **WHEN** the picker opens
- **THEN** both caps show "Unlimited" checked and the run would start without caps

#### Scenario: Setting a cap starts from the estimate
- **WHEN** the administrator unchecks "Unlimited" for max cost while the estimate is $0.883
- **THEN** the max cost field is enabled, holds 0.89, and no over-budget warning is shown

#### Scenario: Only a cost cap
- **WHEN** the administrator sets a cost cap of $1.00 and leaves max tokens unlimited
- **THEN** the start request carries a cost cap of $1.00 and no token cap

#### Scenario: Invalid value
- **WHEN** the administrator enters 0 as max tokens
- **THEN** the field is marked invalid and Start is disabled

### Requirement: Every run limit shown with its default
The model step SHALL show every limit that bounds a run, each filled with its default, served by the api rather than
written into the browser. Editable, with their bounds: max attempts (default 10, from 1 to 10), tool rounds per attempt
(default 40, from 1 to 40), test runs the model may start per attempt (default 2, from 0 to 2), the run deadline in
minutes (default and maximum the configured deadline, 120, from 10) and the suspected bugs a run may report (default 3,
from 0 to 3), next to the two budget caps. Shown read-only: the target line coverage, which is the threshold being
raised. A value outside its bounds, or not a whole number, SHALL be shown as invalid with its bounds, and the Start
control SHALL stay disabled until it is corrected. The limits entered SHALL be sent with the start request.

#### Scenario: Defaults shown
- **WHEN** the model step opens
- **THEN** it shows max attempts 10, tool rounds per attempt 40, test runs per attempt 2, run deadline 120 minutes, suspected bugs 3, both caps unlimited, and the target

#### Scenario: Fewer attempts
- **WHEN** the administrator sets max attempts to 3 and starts the run
- **THEN** the start request carries max attempts 3, and the run reports attempt n of 3

#### Scenario: Above the bound
- **WHEN** the administrator enters 50 tool rounds per attempt
- **THEN** the field is marked invalid, says it must be from 1 to 40, and Start is disabled

#### Scenario: Shorter deadline and no bug reports
- **WHEN** the administrator sets the run deadline to 30 minutes and suspected bugs to 0, and starts the run
- **THEN** the start request carries a deadline of 30 minutes and a suspected-bug limit of 0

#### Scenario: Deadline too short
- **WHEN** the administrator enters a run deadline of 5 minutes
- **THEN** the field is marked invalid, says it must be from 10 to 120, and Start is disabled

#### Scenario: Limits unchanged
- **WHEN** the administrator starts without touching the limits
- **THEN** the run has the default limits
