# tenant-isolation Specification

## Purpose
Guarantees that every request is attributed to an authenticated principal
belonging to exactly one firm, and that retrieval only ever returns that
firm's content plus shared content.

## Requirements

### Requirement: Principal derived from token
The system SHALL derive the caller's principal (user id, firm id, role, and
allowed advisor ids) solely from a bearer JWT issued by the local dev
issuer. Roles SHALL be one of FIRM_ADMIN, ADVISOR, OPS, READ_ONLY. Requests
without a valid token MUST be rejected.

#### Scenario: Valid token yields principal
- **WHEN** a request carries a valid dev-issuer JWT for user U of firm A with role ADVISOR
- **THEN** the request is processed with principal {user U, firm A, role ADVISOR, the token's advisor ids}

#### Scenario: Missing or invalid token
- **WHEN** a request to the chat API or the MCP server carries no token, an expired token, or a token with an invalid signature
- **THEN** the request is rejected with an authentication error and no retrieval is performed

### Requirement: Tenant never taken from untrusted input
The tenant (firm id) MUST NOT be accepted from a request parameter, request
body, tool argument, or model output. No endpoint, tool, or query input SHALL
expose a tenant field.

#### Scenario: Tool argument cannot select a tenant
- **WHEN** a caller from firm A invokes `search_documents` with an extra argument naming firm B
- **THEN** the argument is rejected or ignored and results contain only firm A and shared content

#### Scenario: Tool schemas carry no tenant
- **WHEN** the MCP tool list is inspected
- **THEN** no tool input schema contains a tenant or firm field

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

### Requirement: Other tenants' content never observable
Content belonging to another firm MUST NOT appear in responses, logs, or the
input of any rerank stage for a caller's request.

#### Scenario: Large neighbouring tenant
- **WHEN** a firm A user runs a query whose closest matches globally are firm B documents
- **THEN** firm B content is absent from the response, the audit and application logs, and the reranker input
