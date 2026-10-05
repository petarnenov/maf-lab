# Spec Delta

## ADDED Requirements

### Requirement: Sign-in through the company IdP
Users SHALL sign in with OpenID Connect against the configured company IdP. The api and every in-repository MCP server
SHALL accept only tokens that the IdP's published keys validate. In dev and qa only, the `dev-login` issuer MAY stand
in for the IdP (see `tenant-isolation`), including tokens it mints for a named audience. The principal SHALL be built from the token alone:
- the tenant from its organization;
- its roles;
- user id and group ids from its standard claims.

#### Scenario: A token from another issuer
- **WHEN** a request carries a well-formed token signed by an issuer other than the configured IdP
- **THEN** it is rejected and no retrieval is performed

### Requirement: The operator acts on a tenant through its organization
A principal with the `PLATFORM_ADMIN` role SHALL act on a tenant only with a token requested for that tenant's
organization. No route SHALL take a tenant parameter.

#### Scenario: An operator without the tenant's organization
- **WHEN** an operator's token has no organization and they request a tenant's configuration
- **THEN** the request is refused

### Requirement: Operators read a tenant's content only by break-glass
An operator's token for a tenant SHALL by default grant configuration and counts only; every route that returns a tenant's
content SHALL refuse it. Content access SHALL require a separate grant that:
- states a reason;
- is recorded in the system's own store, never as a claim in the IdP's token;
- expires within one hour and cannot be renewed.

Every grant SHALL be audited with who, which tenant, the reason, and its start and end. The tenant SHALL see every grant in
its dashboard.

#### Scenario: Reading a conversation without a grant
- **WHEN** an operator acting as a tenant without an active content grant requests a conversation
- **THEN** the request is refused

#### Scenario: The tenant sees the access
- **WHEN** an operator was granted content access to a tenant
- **THEN** the tenant's dashboard lists the grant with the operator, the reason and the time

### Requirement: Only supported IdP features in stage and prod
The realm configuration used for stage and prod SHALL enable no preview or experimental Keycloak feature. A check SHALL
fail and name the flag when one is enabled.

#### Scenario: A preview flag
- **WHEN** the stage realm export enables `token-exchange-delegation`
- **THEN** the check fails and names it
