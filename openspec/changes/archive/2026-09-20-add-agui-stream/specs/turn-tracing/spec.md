# Spec Delta

## MODIFIED Requirements

### Requirement: Live streaming of the trace
Trace events SHALL be streamed to the requesting client while the turn runs, interleaved with the run's other
events, so that each step is visible when it happens. They SHALL travel as the event stream's extension point
rather than as a type of its own, so a consumer that does not know about them can still follow the run.

#### Scenario: Trace arrives before the answer completes
- **WHEN** a turn calls `search_documents`
- **THEN** the client receives the trace events for the tool call before the run's terminal event

#### Scenario: A consumer that ignores the trace
- **WHEN** a client drops the trace events
- **THEN** the run still renders from the protocol's own events
