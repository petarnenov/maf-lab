## ADDED Requirements

### Requirement: Focused attempts, whole-suite baseline and confirmation
The agent SHALL measure the baseline on the whole suite, and SHALL keep the target file's covered and uncovered lines
from it.

Each attempt's measured run, and each `run_tests` call, SHALL ask the runner for the related tests only. The target
coverage the agent uses, shows and feeds back for such a run SHALL be computed from the lines:

- a line counts as covered when the baseline covered it or this run covered it;
- the percentage is covered lines over the baseline's executable lines;
- the uncovered ranges are what is left.

The production code does not change during a run, so tests the run did not select still cover what they covered at the
baseline. `run_tests` SHALL tell the model which tests ran and whether the run was focused.

When an attempt is clean on a focused run and reaches the target, the agent SHALL run the whole suite on that attempt's
diff before it stops, and SHALL report the `testing` phase while it does. That run's results SHALL replace the
attempt's:

- coverage measured, not merged;
- the test counts;
- the failures.

If the attempt is no longer clean on them, the loop SHALL go on, and those failures SHALL go to the next attempt as
feedback.

A run resumed from a checkpoint that has no baseline lines SHALL run its attempts and `run_tests` on the whole suite.

#### Scenario: An attempt measured on the related tests
- **WHEN** the baseline covered lines 1–4 of a 10-line target, and attempt 1's focused run covers lines 5–7
- **THEN** attempt 1's coverage is 70%, and its uncovered ranges are lines 8–10

#### Scenario: The model runs the tests
- **WHEN** the model calls `run_tests` in an attempt
- **THEN** the runner is asked for the related tests, and the result tells the model the scope and the test files that ran

#### Scenario: Reaching the target is confirmed on the whole suite
- **WHEN** a focused attempt is clean and reaches the target, and the whole suite on its diff is green
- **THEN** the run stops at the target, with the whole suite's coverage

#### Scenario: The whole suite finds a broken test
- **WHEN** a focused attempt is clean and reaches the target, but another test fails on the whole suite
- **THEN** the attempt is not clean, the run goes on, and the next attempt's input names the failing test

#### Scenario: Resumed from an older checkpoint
- **WHEN** a run resumes from a checkpoint without baseline lines
- **THEN** its attempts run the whole suite
