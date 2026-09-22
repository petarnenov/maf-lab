# shared-state Specification

## Purpose
Where the state lives that every replica must see, what may never live in a replica's memory, and what a replica
does when it cannot reach that state.

## Requirements

### Requirement: State that outlives a request lives where every replica can see it
State that must outlive the request that made it SHALL be kept outside the serving process, in one place every
replica reads and writes. This SHALL cover: a conversation and its messages; what a conversation is waiting on; a
run's state while it runs; the tasks and the webhook registrations of every agent this system hosts; and the
record of which idempotency keys have been processed.

A replica MAY keep something in its own memory only when losing it costs nothing a caller can notice — a cache it
can rebuild, or the cancellation of a request it is itself serving.

#### Scenario: Any replica can answer
- **WHEN** something is written through one replica and asked for through another
- **THEN** the second replica answers with what the first wrote

#### Scenario: A replica's memory is not the store
- **WHEN** a replica is stopped and replaced
- **THEN** nothing a caller can ask for was lost with it

### Requirement: A server-minted handle is self-describing or it is shared
A handle this system hands out and expects back — a cursor, a proposal's state, a continuation of any kind — SHALL
either carry its own meaning, signed so it cannot be altered, or be kept in the shared store. A handle that is a
key into one replica's memory SHALL NOT be issued: it is a session under another name, and the next request will
reach a different replica.

#### Scenario: A cursor on another replica
- **WHEN** a caller pages a list and the next page is served by a different replica
- **THEN** the cursor is understood and the page continues where the last one stopped

#### Scenario: An altered handle
- **WHEN** a handle that carries its own meaning comes back changed
- **THEN** it is refused rather than acted on

### Requirement: A replica that cannot see the shared state says so
A service that needs the shared store SHALL refuse to start without it, rather than start and appear to work. A
store that becomes unreachable while running SHALL make the requests that need it fail plainly, and the service
SHALL report itself as not healthy for as long as it cannot reach it.

#### Scenario: Starting without the store
- **WHEN** a service that needs the shared store starts and cannot reach it
- **THEN** it does not begin serving, and says which store it could not reach

#### Scenario: The store goes away
- **WHEN** the shared store becomes unreachable while a replica is serving
- **THEN** requests that need it fail with a plain error, and the replica reports itself unhealthy

#### Scenario: The store comes back
- **WHEN** the shared store becomes reachable again
- **THEN** the replica serves again without being restarted

### Requirement: What is shared and what is recorded are different stores
The shared store SHALL hold what the replicas need in flight. What is written to be read later — the turns and
their traces, the audit chain, feedback and labels, and the ledger of applied adjustments — SHALL stay in the
store that keeps it, and SHALL NOT depend on the shared store to be readable.

#### Scenario: The shared store is empty
- **WHEN** the shared store is cleared and a past turn's trace is requested
- **THEN** the trace is returned, because it was never kept there

#### Scenario: An applied adjustment
- **WHEN** the shared store is cleared after an adjustment was applied
- **THEN** the account's fee still reflects it
