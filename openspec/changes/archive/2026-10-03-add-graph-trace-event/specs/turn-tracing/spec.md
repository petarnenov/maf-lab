## ADDED Requirements

### Requirement: Graph reads are traced
When retrieval diagnostics are requested (`Agent:TraceRetrieval`), every graph tool call in a turn that read the
graph SHALL add one `graph` event to the turn's trace, right after that call's `tool.result`. A graph tool call that
read nothing, for example one refused for an invalid argument, SHALL add no `graph` event.

The event SHALL carry:
- the tool call it concerns and the tool's name;
- the MCP replica that served it;
- the tenants the graph read path bound for the caller;
- each read in the order it ran, with the query template's name, its row limit, the rows returned, whether the result
  was truncated, how long it took, and its outcome: `ok`, `unavailable` (the graph store could not be reached),
  `cancelled` or `error`. For a read that did not succeed it SHALL also carry the exception's type name;
- the total rows, whether any read was truncated, the total time spent in the graph store, and the call's outcome,
  which is the first outcome that is not `ok`, or `ok`.

The event's duration SHALL be the total time spent in the graph store. Its title SHALL name Neo4j and the templates
that ran, then give the total rows (and `truncated` when a read was truncated) or, when a read did not succeed, its
outcome.

The event MUST NOT contain a tool argument's value, a node property, method keys, query text, an exception message, a
hostname or any credential. The diagnostics SHALL travel in the tool result's metadata under their own key, which
the api SHALL remove from the recorded `tool.result` and from everything the model reads.

#### Scenario: A billing relationship lookup
- **WHEN** a turn calls `trace_billing_relationships` for an account, and the graph returns 7 neighbourhood rows in
  9 ms and 2 firm runs in 3 ms
- **THEN** the trace holds, after that call's `tool.result`, a `graph` event for the same call with two reads,
  `billing_neighbourhood_2` and `firm_runs`, 9 rows in total and a duration of 12 ms
- **AND** its title reads "Neo4j billing_neighbourhood_2 + firm_runs · 9 rows"

#### Scenario: Code graph tools
- **WHEN** a turn calls `trace_code_symbol` and then `change_impact`
- **THEN** each call has its own `graph` event, naming `symbol_candidates` and `callers_2` for the first, and
  `file_methods` and `callers_4` for the second

#### Scenario: A truncated read
- **WHEN** a read reaches more rows than its limit
- **THEN** that read is marked truncated, the event is marked truncated, and the title says "truncated"

#### Scenario: Graph store unavailable
- **WHEN** the graph store is stopped and the agent calls a graph tool
- **THEN** the tool still returns its safe error, and the `graph` event records the read with the outcome
  `unavailable` and the exception's type name
- **AND** the title reads like "Neo4j billing_neighbourhood_2 · unavailable", and the event holds no hostname and no
  exception message

#### Scenario: Arguments stay in the tool call
- **WHEN** a turn calls `trace_billing_relationships` for `A-1042`
- **THEN** `A-1042` appears in the `tool.call` event's arguments and nowhere in the `graph` event

#### Scenario: The model never reads the diagnostics
- **WHEN** a graph tool returns its reads in the result's metadata
- **THEN** neither the recorded `tool.result` nor the data envelope holds that metadata key

#### Scenario: Diagnostics not requested
- **WHEN** retrieval diagnostics are turned off and a turn calls a graph tool
- **THEN** the trace holds no `graph` event, and the tool's result is unchanged

#### Scenario: A trace from before the graph event
- **WHEN** a stored trace recorded before this kind existed is read
- **THEN** it is returned unchanged, with no `graph` event
