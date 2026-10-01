# Spec Delta

## ADDED Requirements

### Requirement: Model-written changes are held to the lint bar CI applies
When a request carries a diff, the runner SHALL check the files the diff adds or changes against the lint CI applies
to them, and SHALL report every finding as a build diagnostic that names the file and line, with the build outcome
failed:

- for `dotnet`, each compiler or analyzer warning the build reports in one of those files, as a build with warnings
  as errors would fail on it;
- for `vitest`, each ESLint error the repository's ESLint configuration reports in one of those files, and each of
  those files that Prettier, with the repository's configuration, would format differently; the Prettier diagnostic
  SHALL name the first line that differs and say how Prettier writes it.

Warnings in files the diff does not add or change SHALL NOT fail the build. ESLint warnings SHALL NOT fail it. A
lint failure SHALL keep what the run measured (test counts, failures, the target file's coverage). When the lint
check cannot run, the build SHALL be reported failed with a diagnostic saying so. A request without a diff SHALL be
built, tested and measured without this check.

#### Scenario: A test that triggers CA2022
- **WHEN** a `dotnet` request's diff adds a test file whose build reports `warning CA2022` on line 9
- **THEN** the build is reported failed, its diagnostics carry that file, line and `warning CA2022`, and the test
  counts and the target file's coverage are still returned

#### Scenario: A warning outside the diff
- **WHEN** a `dotnet` build reports a warning in a file the diff does not add or change, and none in the diff's files
- **THEN** the build is reported ok and the warning is not among the diagnostics

#### Scenario: No diff, warnings present
- **WHEN** a request without a diff builds with warnings
- **THEN** the build is reported ok and the run is measured as before

#### Scenario: An ESLint error in a changed web test
- **WHEN** a `vitest` request's diff adds a test file with a variable that is assigned and never used
- **THEN** the build is reported failed with a diagnostic naming the file, the line and the ESLint rule

#### Scenario: A web test Prettier would format differently
- **WHEN** a `vitest` request's diff adds a test file that uses double quotes where the configuration asks for single
- **THEN** the build is reported failed with a diagnostic naming the file, the first line that differs, and that line
  as Prettier writes it

#### Scenario: Only ESLint warnings
- **WHEN** the ESLint configuration reports only warnings for a changed web file, and Prettier finds nothing
- **THEN** the build is reported ok

#### Scenario: The lint tools are missing
- **WHEN** a `vitest` request carries a diff and the web app's dependencies are not installed in the workspace
- **THEN** the build is reported failed with a diagnostic saying the lint check could not run
