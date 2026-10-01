## ADDED Requirements

### Requirement: Which tests verification runs
The api SHALL prove each suspected bug with a runner job for the related tests only: the diff with that test
un-skipped, the run's file as the target. The test is in a file the diff adds or changes, so it is always among them.

The verification run SHALL run the whole suite. A diff whose tests break, or are broken by, a test it did not touch
then still ends `verification_failed`. The candidate's coverage SHALL be that run's measured coverage. Coverage
refresh SHALL also run the whole suite.

#### Scenario: Proof runs the related tests
- **WHEN** a completed run reports one suspected bug
- **THEN** the api asks the runner for the related tests with that test un-skipped, and the test is among them

#### Scenario: Verification runs everything
- **WHEN** the api verifies a completed run
- **THEN** the measured run's scope is the whole suite, and a failing test anywhere in it ends the run
  `verification_failed`
