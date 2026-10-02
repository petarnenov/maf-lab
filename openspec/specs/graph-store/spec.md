# graph-store Specification

## Purpose
Provides a graph database next to the vector store, so the lab can answer relationship questions about the billing
domain and the codebase. It is filled by an idempotent, progress-reporting build and read only through a
tenant-scoped path.

## Requirements

### Requirement: Graph store service
The stack SHALL run one graph database service, started by `make` with the rest of the stack, with persistent storage
and a healthcheck. Its query port SHALL be reachable only from inside the compose network and from the host's loopback interface, where
the host-side indexer builds the graph. Its credentials SHALL come
from the environment, with a development default, and MUST NOT appear in logs, traces, tool results or the topology
report. The services that read it SHALL start only once it is healthy.

#### Scenario: The stack starts with the graph store
- **WHEN** `make` brings the stack up
- **THEN** the graph store reports healthy before the billing and codebase MCP servers start

#### Scenario: Data survives a restart
- **WHEN** the graph has been built and the stack is restarted with `make down` and `make`
- **THEN** the graph still holds the same nodes and is not rebuilt

#### Scenario: Query port is not published
- **WHEN** the host's published ports are listed
- **THEN** the graph query port is not published on any interface other than loopback

### Requirement: Graph browser as a dev inspector
The graph database's own browser SHALL be offered as a development inspector on `http://localhost:7175`, bound to
loopback only, under the same profile as the other inspectors, so it is off in CI.

#### Scenario: Inspector in development
- **WHEN** the stack runs with the inspectors profile
- **THEN** the graph browser answers on `http://localhost:7175` and is not reachable from other hosts

#### Scenario: No inspector in CI
- **WHEN** the stack runs with `CI_MODE=1`
- **THEN** no graph browser port is published

### Requirement: One tenant-scoped graph read path
Every read from the graph store SHALL go through exactly one method that takes the caller's principal and one query
from a fixed, named set defined in code, together with that query's typed arguments. The method SHALL bind the
principal's readable tenants itself. No caller SHALL supply query text or tenant values. Each query SHALL bound its
traversal depth and the number of nodes it returns.

#### Scenario: Only named queries run
- **WHEN** a component asks to read the graph
- **THEN** it can name only one of the queries defined in code and pass that query's typed arguments, and it has no way
  to pass query text or a tenant

#### Scenario: Results are bounded
- **WHEN** a query reaches more nodes than its limit
- **THEN** it returns at most the limit and marks the result as truncated

### Requirement: One graph maintenance path
Every write to the graph store SHALL go through one maintenance component, whose operations name the tenant the
written nodes belong to. Every node written SHALL carry its tenant, either a firm id or `shared`. A node without a
tenant MUST be rejected.

#### Scenario: Node without a tenant
- **WHEN** a build step produces a node with no tenant
- **THEN** the node is not written, and the build reports it as rejected

### Requirement: Graph build command
The indexer SHALL offer a `graph` command that builds the billing graph and the code graph, and `make graph` SHALL run
it. The build SHALL be idempotent: nodes and edges SHALL be matched by stable keys, and nodes from a source that no
longer produces them SHALL be removed. `make index` SHALL also build the graph, and the empty-index check run by
`make` SHALL build it when the graph store holds no nodes.

#### Scenario: Rebuild without changes
- **WHEN** `make graph` runs twice over an unchanged corpus and repository
- **THEN** the second run creates and deletes nothing, and the node and edge counts are unchanged

#### Scenario: A removed source loses its nodes
- **WHEN** a seed account is removed and `make graph` runs
- **THEN** that account's node and its edges are gone, and the other nodes are untouched

#### Scenario: Empty graph on first start
- **WHEN** `make` starts the stack and the graph store holds no nodes
- **THEN** the graph is built before the stack is reported ready

### Requirement: The graph build shows its progress
The `graph` command SHALL show a progress bar with the current phase (billing or code) and items processed out of the
total, redrawn in place in a terminal and as throttled plain lines in CI, ending with one summary line of nodes and
edges written, unchanged and removed. Machine-readable output SHALL stay separate from the progress output.

#### Scenario: Terminal build
- **WHEN** `make graph` runs in a terminal
- **THEN** a progress bar shows the phase and progress, and the run ends with a summary line

#### Scenario: CI build
- **WHEN** the `graph` command runs with no terminal attached
- **THEN** it prints progress lines at most every few seconds, and one final summary line

### Requirement: Graph store unavailable is a safe error
When the graph store cannot be reached, graph tools SHALL return a short error saying graph lookups are temporarily
unavailable, with no hostnames, query text or stack traces. The MCP server SHALL keep serving its other tools. The
`graph` command SHALL name the unreachable service and exit non-zero, without a stack trace.

#### Scenario: Graph down during a chat turn
- **WHEN** the graph store is stopped and the agent calls a graph tool
- **THEN** the tool returns the unavailable error, and `search_documents` on the same server still answers

#### Scenario: Graph down during a build
- **WHEN** the graph store is stopped and `make graph` runs
- **THEN** the command reports that the graph store is unreachable and exits non-zero

### Requirement: Graph logs carry structure only
Graph reads and writes SHALL be logged and traced with the query name, duration, row and node counts and the outcome,
never with node properties, document text or argument values that came from a chat message.

#### Scenario: A traced graph lookup
- **WHEN** a graph tool runs during a chat turn
- **THEN** its span carries the query name, duration and result count, and no account names, ids from the message, or
  document text
