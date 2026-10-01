## ADDED Requirements

### Requirement: Unreachable infrastructure is named, not thrown
When the indexer cannot connect to Qdrant or to the embedding endpoint, it SHALL end its progress bar in the failure
line, then print one line on stderr naming the service that is unreachable and the address it tried, and telling the
developer to run `make infra`; it SHALL exit with code 1. It SHALL NOT print a stack trace or an unhandled-exception
report, and SHALL NOT abort the process (no core dump). The line SHALL NOT carry exception message text, document
content or secrets.

#### Scenario: Qdrant down
- **WHEN** `make index` would run the indexer while nothing listens on Qdrant's address
- **THEN** stderr ends with a line such as `✗ Qdrant is not reachable at localhost:6334 — run 'make infra' (or 'make') and try again` and the exit code is 1

#### Scenario: Embedding endpoint down
- **WHEN** Qdrant is reachable but the embedding endpoint refuses connections
- **THEN** the line names the embedding endpoint and its address, and the exit code is 1

#### Scenario: Any other failure
- **WHEN** the run fails for a reason other than an unreachable service
- **THEN** the behavior is unchanged: the bar's failure line names the exception type and the exit code is non-zero
