# Spec Delta

## MODIFIED Requirements

### Requirement: Access scope
A trace MUST only be readable by the user who owns the turn and, for turns in their tenant's review queue, by a
TENANT_ADMIN of the same tenant. Users of other tenants MUST receive not found. Trace data MUST NOT be written to logs.

#### Scenario: Other user
- **WHEN** a different user of the same tenant (not TENANT_ADMIN) requests another user's trace
- **THEN** the response is not found

#### Scenario: Other firm's admin
- **WHEN** a TENANT_ADMIN of tenant B requests a trace of a tenant A turn
- **THEN** the response is not found

#### Scenario: Logs stay clean
- **WHEN** a traced turn completes
- **THEN** no log line contains the question, the answer, snippets or prompt text

### Requirement: The frames are kept and served with the turn's trace
The frames of a run that produced a turn SHALL be stored with that turn and returned by
`GET /api/turns/{turnId}/trace` together with the trace's events. They SHALL be readable exactly by whoever may
read that turn's trace, and SHALL be deleted when that trace is deleted, whether by retention or otherwise. A run
that produces no turn — an answer to a confirmation — SHALL NOT have its frames stored, and the response for a
turn that has none SHALL say that they were not recorded rather than report an empty run.

#### Scenario: Reopen a stored turn
- **WHEN** a user opens an earlier assistant turn of their conversation
- **THEN** the trace response carries that turn's recorded frames alongside its trace events

#### Scenario: Another firm's admin
- **WHEN** a TENANT_ADMIN of tenant B requests a tenant A turn's trace
- **THEN** the response is not found, and no frame of that run is disclosed

#### Scenario: Retention
- **WHEN** the retention job deletes a turn's trace
- **THEN** that turn's frames are deleted with it

#### Scenario: A run with no turn
- **WHEN** a run answers a confirmation and so records no turn
- **THEN** its frames are not stored, and no stored turn reports them

#### Scenario: Frames stay out of the logs
- **WHEN** a run's frames are recorded
- **THEN** no log line contains a frame's payload
