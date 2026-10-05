# Spec Delta

## ADDED Requirements

### Requirement: A stopped graph query is ended in Neo4j
Every query the graph classes run SHALL be run so that its caller's cancellation ends it on the server, not only on the
client (stop-anything): the query's transaction carries a stop id, and a cancel ends that transaction with Neo4j's own
`TERMINATE TRANSACTIONS`. A test SHALL fail the build when a call to the Neo4j driver in the graph classes is not part
of a query run that way, the terminate statement itself aside.

#### Scenario: A query written around the stop
- **WHEN** a graph class calls the driver directly, outside the query run that carries the stop id
- **THEN** the architecture test fails and names the method

#### Scenario: A cancelled read
- **WHEN** a graph read is cancelled while Neo4j runs it
- **THEN** the call ends at once and the server no longer lists its transaction
