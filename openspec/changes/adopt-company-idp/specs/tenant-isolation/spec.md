# Spec Delta

## MODIFIED Requirements

### Requirement: Principal derived from token
The system SHALL derive the caller's principal (user id, tenant id, core role and group ids) solely from a bearer token
issued by the configured company IdP and validated through its published keys. The tenant SHALL come from the token's
organization. Core roles SHALL be one of TENANT_ADMIN, USER, READ_ONLY, PLATFORM_ADMIN. Domain roles and attributes
SHALL be claims that the core does not read; only the domain's own server reads them. In dev and qa only, the
`dev-login` plugin's issuer MAY stand in for the IdP, and for one release `firm_id` MAY stand in for `tenant_id` in its
tokens (from `rename-firm-to-tenant`). Requests without a valid token MUST be rejected.

#### Scenario: Valid token yields principal
- **WHEN** a request carries a valid IdP token for user U of organization A with core role USER
- **THEN** the request is processed with principal {user U, tenant A, role USER, the token's groups}

#### Scenario: Missing or invalid token
- **WHEN** a request to the chat API or the MCP server carries no token, an expired token, or a token with an invalid signature
- **THEN** the request is rejected with an authentication error and no retrieval is performed

#### Scenario: A token from before the rename
- **WHEN** in dev, a request carries a valid dev-issuer token with `firm_id` and no `tenant_id`
- **THEN** the principal's tenant is that `firm_id`

#### Scenario: The dev issuer outside dev and qa
- **WHEN** `MAF_ENV` is stage or prod
- **THEN** a token from the dev issuer is rejected
