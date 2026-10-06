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
The graph database's own browser SHALL be offered by its plugin, `neo4j-browser` (allowed in dev and qa only), on
`http://localhost:7175`, bound to loopback only, so it is absent wherever the plugin is not installed, CI included.

#### Scenario: Inspector in development
- **WHEN** the stack runs with the `neo4j-browser` plugin installed
- **THEN** the graph browser answers on `http://localhost:7175` and is not reachable from other hosts

#### Scenario: No inspector in CI
- **WHEN** the stack runs with `CI_MODE=1`
- **THEN** no graph browser port is published

### Requirement: One tenant-scoped graph read path
Every read from the graph store made on behalf of a request, a tool or the model SHALL go through exactly one method.
That method takes the caller's principal and one query from a fixed, named set defined in code, together with that
query's typed arguments. The method SHALL bind the principal's readable tenants itself. No caller SHALL supply query
text or tenant values. Each query SHALL bound its traversal depth and the number of nodes it returns.

The only other reads SHALL be the maintenance reads of the graph maintenance path, which the build and drift
reporting use: counts, stale-node removal and the listing of billing document nodes. Each maintenance read SHALL run
fixed query text defined in code, take no query text and no tenant value from its caller, and return keys, tenants
and hashes, never free text. No graph tool and no model-facing code SHALL reach a maintenance read.

#### Scenario: Only named queries run
- **WHEN** a component asks to read the graph
- **THEN** it can name only one of the queries defined in code and pass that query's typed arguments, and it has no way
  to pass query text or a tenant

#### Scenario: Results are bounded
- **WHEN** a query reaches more nodes than its limit
- **THEN** it returns at most the limit and marks the result as truncated

#### Scenario: Maintenance reads stay off the request path
- **WHEN** the query-path check scans the services that host graph tools
- **THEN** no tool or agent code calls a maintenance read, and only the read-path method and the maintenance component
  open a graph session

### Requirement: One graph maintenance path
Every write to the graph store SHALL go through one maintenance component, whose operations name the tenant the
written nodes belong to. Every node written SHALL carry its tenant, either a firm id or `shared`. A node without a
tenant MUST be rejected.

The same component SHALL list the billing document nodes, each with its tenant, document id and source content hash,
for drift reporting. The caller filters the list to the tenants it may report.

#### Scenario: Node without a tenant
- **WHEN** a build step produces a node with no tenant
- **THEN** the node is not written, and the build reports it as rejected

#### Scenario: Listing document nodes
- **WHEN** drift reporting lists the billing document nodes after a build
- **THEN** it receives one entry per document node, with its tenant, document id and source content hash, and no
  title, path or text

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
never with node properties, document text or argument values that came from a chat message. This SHALL hold for the
turn trace as well: the read path SHALL record each read for the turn trace at the point where it binds the tenants,
from the same template name, row count, truncation, duration and outcome that its span carries, and with nothing
more than those and the tenants it bound.

#### Scenario: A traced graph lookup
- **WHEN** a graph tool runs during a chat turn
- **THEN** its span carries the query name, duration and result count, and no account names, ids from the message, or
  document text

#### Scenario: The turn trace's graph event matches the span
- **WHEN** a graph tool runs during a chat turn with diagnostics requested
- **THEN** the turn trace's `graph` event names the same templates, with the same row counts and truncation as their
  `graph.read` spans, and holds no id from the message, no node property and no document text

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
