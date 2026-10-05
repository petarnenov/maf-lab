# Spec Delta

## MODIFIED Requirements

### Requirement: Principal derived from token
The system SHALL derive the caller's principal (user id, tenant id and core role) solely from a bearer JWT issued by
the local dev issuer. Core roles SHALL be one of TENANT_ADMIN, USER, READ_ONLY. The tenant SHALL be read from the
`tenant_id` claim; for one release, a token with `firm_id` and no `tenant_id` SHALL be accepted, with that value as
its tenant. Domain roles and attributes (such as a billing advisor's ids) SHALL be claims that the core does not read;
only the domain's own server reads them. Requests without a valid token MUST be rejected.

#### Scenario: Valid token yields principal
- **WHEN** a request carries a valid dev-issuer JWT for user U of tenant A with core role USER and the domain role
  `billing:advisor`
- **THEN** the api processes it with principal {user U, tenant A, role USER}, and the billing server scopes its results
  to the token's advisor ids

#### Scenario: Missing or invalid token
- **WHEN** a request to the chat API or the MCP server carries no token, an expired token, or a token with an invalid signature
- **THEN** the request is rejected with an authentication error and no retrieval is performed

#### Scenario: A token from before the rename
- **WHEN** a request carries a valid token with `firm_id` and no `tenant_id`
- **THEN** the principal's tenant is that `firm_id`

### Requirement: Tenant never taken from untrusted input
The tenant (tenant id) MUST NOT be accepted from a request parameter, request
body, tool argument, or model output. No endpoint, tool, or query input SHALL
expose a tenant field.

#### Scenario: Tool argument cannot select a tenant
- **WHEN** a caller from tenant A invokes `search_documents` with an extra argument naming tenant B
- **THEN** the argument is rejected or ignored and results contain only tenant A and shared content

#### Scenario: Tool schemas carry no tenant
- **WHEN** the MCP tool list is inspected
- **THEN** no tool input schema contains a tenant field

### Requirement: Mandatory tenant filter on every retrieval
Every query to the vector store and to the graph store SHALL be restricted to the principal's tenant and the shared
corpus. For the graph store, the restriction SHALL hold for every node a query matches or returns, not only its
starting node, so that no path can cross into another tenant's nodes. There MUST be no query path to either store that
executes without this restriction, and this MUST be verified by an automated test that enumerates all query paths.
Graph queries SHALL come only from a fixed set of parameterised queries defined in code; no query text SHALL be taken
from a request, a tool argument or model output.

#### Scenario: Advisor sees only own firm and shared
- **WHEN** a user of tenant A asks a procedural question
- **THEN** every returned chunk has tenant tenant A or "shared"

#### Scenario: Query-path enumeration test
- **WHEN** the tenant-isolation test suite runs
- **THEN** it discovers every code path that queries the vector store or the graph store and fails if any path does
  not apply the tenant filter

#### Scenario: Graph traversal stops at the tenant boundary
- **WHEN** a tenant A user traces the relationships of a tenant A account, and the graph contains a tenant B node linked to
  a shared node that the tenant A account also reaches
- **THEN** the result contains only tenant A and shared nodes, and the tenant B node and its edges are absent

#### Scenario: Every graph query restricts every matched node
- **WHEN** the tenant-isolation test suite inspects the fixed set of graph queries
- **THEN** it fails if any node a query matches is not restricted to the readable tenants

#### Scenario: A rogue graph query path is detected
- **WHEN** a test fixture adds code that opens a graph session outside the tenant-scoped read and maintenance paths
- **THEN** the enumeration test reports it

### Requirement: Other tenants' content never observable
Content belonging to another tenant MUST NOT appear in responses, logs, or the
input of any rerank stage for a caller's request.

#### Scenario: Large neighbouring tenant
- **WHEN** a tenant A user runs a query whose closest matches globally are tenant B documents
- **THEN** tenant B content is absent from the response, the audit and application logs, and the reranker input
