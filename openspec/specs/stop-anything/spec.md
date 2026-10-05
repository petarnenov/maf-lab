# stop-anything Specification

## Purpose
Everything the system does for a person can be stopped by that person: with Esc on the page that started it, with
Ctrl+C in a terminal. A stop reaches every party doing the work, only by each protocol's own means, and work that
outlives its request is stopped through the store that owns its state, from any replica.

## Requirements

### Requirement: Esc stops what the page started
On every page, Esc SHALL stop the work that page started and that is still running: an agent run, a job, a run it
follows on someone's behalf, or a request still loading. A person who may not stop a piece of work SHALL NOT be
offered the stop, and Esc SHALL then do what it does on that page otherwise.
- An Esc a control has already handled for itself (closing a menu, cancelling an edit) or pressed while an input
  method is composing SHALL NOT stop anything.
- With nothing running, Esc SHALL stop nothing.
- Leaving a page SHALL abort the requests it still has open, the same way.
- While a page runs work, it SHALL show that Esc stops it, in the page's theme.

#### Scenario: A job on an admin screen
- **WHEN** an administrator starts an index run and presses Esc
- **THEN** the run is asked to stop, the page says it is stopping, and then shows it stopped

#### Scenario: A slow read
- **WHEN** a page is still loading a report that takes several seconds and the person presses Esc or leaves the page
- **THEN** the request is aborted, and the server stops the work it was doing for it

#### Scenario: Not allowed to stop it
- **WHEN** a person who may not cancel a run watches it and presses Esc
- **THEN** nothing is stopped

### Requirement: Ctrl+C stops what a tool started
Every CLI tool and every make target SHALL stop on Ctrl+C (SIGINT) and on SIGTERM:
- at a safe point — between items, never half-way through a step that must be written whole;
- cancelling any server work it started (an admin job, a test-generation run) before it exits;
- ending with one line that says it was cancelled, what was done and what to run again, and exit code 130.

#### Scenario: Indexing interrupted
- **WHEN** a developer presses Ctrl+C during `make index`
- **THEN** the document being written is finished, no further document is started, the tool says how many documents
  were indexed and to run `make index` again, and it exits 130

#### Scenario: A script that started server work
- **WHEN** a developer presses Ctrl+C while a script waits on a coverage refresh it started
- **THEN** the script cancels that job before it exits, and the job ends canceled

### Requirement: A stop travels only by the protocols' own means
A stop SHALL use only the mechanism the protocol in use defines: CopilotKit's stop for an AG-UI run; the abort of the
HTTP request for a request (and from there the request's `CancellationToken` into MCP, Qdrant and Neo4j); A2A
`tasks/cancel` for an agent's task; this system's own cancel route for its own jobs. No event, message, field or
request SHALL be invented for stopping, and no AG-UI event SHALL be built outside the official libraries.

#### Scenario: A chat run with a search in flight
- **WHEN** a chat run is stopped while it is searching documents
- **THEN** the search is cancelled on the MCP server and its Qdrant query is cancelled, with no request beyond the
  protocols'

### Requirement: Work that outlives its request is stopped through its store
Work that goes on after the request that started it (an A2A task, an admin job, a test-generation run, a coverage
runner job) SHALL be stopped through the store that owns its state:
- the stop SHALL be recorded as a terminal state in that store, and the store SHALL never let a later write replace a
  terminal state with another; the check and the write SHALL be one atomic operation in the store;
- the process running the work SHALL notice the recorded stop within a short interval and stop at its next safe
  point;
- any replica SHALL accept the stop; no routing to the replica running the work SHALL be required.

#### Scenario: Stopped through another replica
- **WHEN** a job runs on one api replica and its cancel is received by the other
- **THEN** the job ends canceled, stops working, and is still canceled after it would have finished

#### Scenario: A late write
- **WHEN** a task is canceled in its store and the process that ran it then saves it as completed
- **THEN** the store keeps it canceled

### Requirement: Stores stop their part
A cancelled call to Qdrant or Neo4j SHALL end at once on the caller's side, and SHALL NOT leave work running in the
store that the caller no longer waits for: a cancelled Neo4j query's transaction SHALL be gone from the server, and a
cancelled Qdrant call SHALL be cancelled on its gRPC channel. Every call to either store SHALL carry the caller's
`CancellationToken`.

#### Scenario: A long graph read
- **WHEN** a graph read is cancelled while Neo4j runs it
- **THEN** the call ends at once, and the server lists no transaction for it

### Requirement: A stopped thing says so
A stop SHALL be shown as a stop, not as a failure: while it is on its way the page or the terminal SHALL say it is
stopping; once the work has stopped it SHALL say so in the page's design or as the tool's final line, per
progress-feedback. A stop SHALL NOT be shown before the work's own state says it has stopped.

#### Scenario: Stopping, then stopped
- **WHEN** a person stops a test-generation run
- **THEN** the page says "Stopping…" until the run reports canceled, then shows it canceled, with no error

### Requirement: Changes are checked against the rule
Every proposal that adds or changes work a person can start SHALL say how it is stopped (the key, the protocol's stop,
the store that records it) and how a stop is shown; review SHALL reject one that does not.

#### Scenario: A new admin action
- **WHEN** a proposal adds an admin action that runs longer than a moment
- **THEN** it names its Esc stop, its cancel route and the state its stop is recorded in, and review rejects it otherwise
