# Spec Delta

## MODIFIED Requirements

### Requirement: Principal derived from token
The system SHALL derive the caller's principal (user id, tenant id and core role) solely from a bearer JWT issued by
the local dev issuer. Core roles SHALL be one of TENANT_ADMIN, USER, READ_ONLY. The tenant SHALL be read from the
`tenant_id` claim; for one release, a token with `firm_id` and no `tenant_id` SHALL be accepted, with that value as
its tenant. Domain roles and attributes (such as a billing advisor's ids) SHALL be claims that the core does not read;
only the domain's own server may read them. Requests without a valid token MUST be rejected.

#### Scenario: Valid token yields principal
- **WHEN** a request carries a valid dev-issuer JWT for user U of tenant A with core role USER, the domain role
  `billing:advisor` and advisor ids
- **THEN** the api processes it with principal {user U, tenant A, role USER}, which holds no domain role or advisor id;
  those claims travel in the token for the domain's own server to read, and no server reads them today

#### Scenario: Missing or invalid token
- **WHEN** a request to the chat API or the MCP server carries no token, an expired token, or a token with an invalid signature
- **THEN** the request is rejected with an authentication error and no retrieval is performed

#### Scenario: A token from before the rename
- **WHEN** a request carries a valid token with `firm_id` and no `tenant_id`
- **THEN** the principal's tenant is that `firm_id`
