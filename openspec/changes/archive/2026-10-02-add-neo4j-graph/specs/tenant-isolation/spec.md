## MODIFIED Requirements

### Requirement: Mandatory tenant filter on every retrieval
Every query to the vector store and to the graph store SHALL be restricted to the principal's firm and the shared
corpus. For the graph store, the restriction SHALL hold for every node a query matches or returns, not only its
starting node, so that no path can cross into another firm's nodes. There MUST be no query path to either store that
executes without this restriction, and this MUST be verified by an automated test that enumerates all query paths.
Graph queries SHALL come only from a fixed set of parameterised queries defined in code; no query text SHALL be taken
from a request, a tool argument or model output.

#### Scenario: Advisor sees only own firm and shared
- **WHEN** an ADVISOR of firm A asks a procedural question
- **THEN** every returned chunk has tenant firm A or "shared"

#### Scenario: Query-path enumeration test
- **WHEN** the tenant-isolation test suite runs
- **THEN** it discovers every code path that queries the vector store or the graph store and fails if any path does
  not apply the tenant filter

#### Scenario: Graph traversal stops at the tenant boundary
- **WHEN** a firm A user traces the relationships of a firm A account, and the graph contains a firm B node linked to
  a shared node that the firm A account also reaches
- **THEN** the result contains only firm A and shared nodes, and the firm B node and its edges are absent

#### Scenario: Every graph query restricts every matched node
- **WHEN** the tenant-isolation test suite inspects the fixed set of graph queries
- **THEN** it fails if any node a query matches is not restricted to the readable tenants

#### Scenario: A rogue graph query path is detected
- **WHEN** a test fixture adds code that opens a graph session outside the tenant-scoped read and maintenance paths
- **THEN** the enumeration test reports it
