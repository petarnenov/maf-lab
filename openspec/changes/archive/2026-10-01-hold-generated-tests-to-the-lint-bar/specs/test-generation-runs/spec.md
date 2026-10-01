# Spec Delta

## MODIFIED Requirements

### Requirement: Independent verification
When the task completes with a non-empty diff, the api SHALL NOT trust the coverage numbers the agent reported. It
SHALL:

1. check that the diff touches only allowlisted test paths;
2. re-check the test guardrails on the diff;
3. have the coverage runner apply the diff at the task's commit and run the toolchain's tests with coverage in a fresh
   workspace;
4. require the build, including the lint bar the runner holds the diff's files to, and every test to pass.

If any step fails, the run SHALL end `verification_failed` with the reason; a build that failed only on lint
diagnostics SHALL be named as not passing lint. Coverage SHALL NOT change. If all steps pass, the api SHALL ingest the
runner's report as a candidate snapshot linked to the run. The run that proves a suspected bug, whose diff is the
candidate's with that one test un-skipped and is never merged, SHALL be judged by its tests alone: lint diagnostics on
it SHALL NOT fail the check.

#### Scenario: Verification failure
- **WHEN** the agent reports 88% but the runner's run has a failing test
- **THEN** the run ends `verification_failed` with that reason and the file's coverage is unchanged

#### Scenario: A candidate that would fail lint
- **WHEN** every test passes but the runner reports the build failed with only a `warning CA2022` diagnostic in the
  diff's test file
- **THEN** the run ends `verification_failed` with the reason that the tests do not pass lint, and no candidate is
  recorded

#### Scenario: A suspected bug's proof with a lint finding
- **WHEN** the run that un-skips a suspected bug's test fails that test and reports only lint diagnostics
- **THEN** the bug counts as reproduced and verification goes on to measure the candidate

#### Scenario: Diff outside the allowlist
- **WHEN** the returned diff modifies a production file
- **THEN** the run ends `verification_failed` without running anything

#### Scenario: Measured, not reported
- **WHEN** the agent reports 88% and the runner measures 84%
- **THEN** the candidate shows 84%
