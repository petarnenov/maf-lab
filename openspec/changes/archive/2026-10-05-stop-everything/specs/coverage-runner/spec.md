# Spec Delta

## ADDED Requirements

### Requirement: A job can be cancelled
The runner SHALL accept a cancel for a job by its id. A cancelled job SHALL stop its build and test processes with
their whole process tree, remove its workspace, and end `canceled`; a result it had not finished SHALL NOT be offered
for reuse. A job joined by several callers SHALL be cancelled only when every caller has cancelled. The api SHALL
cancel the runner job when the work that asked for it is stopped.

#### Scenario: Cancelled mid-build
- **WHEN** a job is building and its only caller cancels it
- **THEN** the build process and its children are gone, the workspace is removed, and the job reads `canceled`

#### Scenario: A joined job
- **WHEN** two callers share a job and one of them cancels
- **THEN** the job goes on for the other
