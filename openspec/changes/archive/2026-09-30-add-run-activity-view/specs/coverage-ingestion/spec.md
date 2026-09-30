## ADDED Requirements

### Requirement: Refresh outcome names its reason
A refresh that does not succeed SHALL end with a summary that names the reason in user-facing words, without
internal detail. The reason SHALL be one of: interrupted (the service stopped while it ran), coverage runner
unavailable, no report produced by either toolchain, `main` has no commit, or failed for another reason. An
interrupted refresh SHALL be distinguishable from a failed one without reading server logs.

#### Scenario: Service stops mid-refresh
- **WHEN** the api is stopped while a refresh is running
- **THEN** the refresh job ends with the interrupted reason, not the generic failure

#### Scenario: Runner down
- **WHEN** a refresh is requested while the coverage runner cannot be reached
- **THEN** the refresh job ends failed with the reason coverage runner unavailable
