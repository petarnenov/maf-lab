## ADDED Requirements

### Requirement: A test result is read only from a complete run
The runner SHALL NOT report a test run as passing unless the run's output shows that every test ran and passed.
When a child process prints more than the runner keeps, the runner SHALL keep both the beginning and the end of the
output, and SHALL mark in the kept text how many lines were left out, so that the summary a test run prints last is
never the part that is lost. For `dotnet`, the failed count SHALL be at least the number of failed tests the output
names, whatever the summary says. A run that built and printed no test summary SHALL be reported with at least one
failure, whose message says the run printed no summary, and never as zero failures.

#### Scenario: Output over the cap keeps its end
- **WHEN** a child process prints far more than the cap, starting with a first line and ending with `failed: 4`
- **THEN** the kept output starts with the first line, ends with `failed: 4`, says that lines were omitted, and stays
  within the cap

#### Scenario: Summary lost, failures named
- **WHEN** a `dotnet` run's output names two failed tests and carries no summary
- **THEN** the build is reported as ok and the run as having 2 failed tests

#### Scenario: No summary and nothing named
- **WHEN** a `dotnet` run builds and prints neither a summary nor any failed test
- **THEN** the run is reported with 1 failed test whose message says it printed no summary
