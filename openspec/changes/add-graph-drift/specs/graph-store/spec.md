## MODIFIED Requirements

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
