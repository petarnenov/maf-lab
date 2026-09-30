# Spec Delta

## MODIFIED Requirements

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
