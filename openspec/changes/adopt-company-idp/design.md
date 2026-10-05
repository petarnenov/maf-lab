# Design

## Context

Decisions taken with the user on 2026-10-05, moved here from `introduce-plugins`. Their letters are kept.

### 5r. Identity comes from the company's IdP (stated by the user, 2026-10-05)

The company is testing an identity provider of its own, and authentication and authorization will come from it. The
assistant therefore hosts no identity provider: no Keycloak, no identity code of its own. It integrates with that IdP
through the standards the IdP supports.

The dev issuer and `DevTokenPicker` become a dev/qa-only plugin (`dev-login`), which keeps a working sign-in for local
development and is absent from stage and prod.

The IdP is based on Keycloak (stated by the user). How each need maps to Keycloak:

- **Supported natively:** OIDC and SAML; tenant and role claims through protocol mappers; Organizations, which map to
  tenants; audience restriction through client scopes and audience mappers.
- **Depends on the Keycloak version and the features enabled:** see the 26.8 table below.
- **Not Keycloak's job:** protected resource metadata (RFC 9728) is served by our MCP servers, the resource servers,
  through the MCP SDK.
- **Provisioning:** SCIM, native and supported since 26.8.

**Keycloak 26.8.0** (released 2026-10-01, `quay.io/keycloak/keycloak:26.8.0`), checked against its documentation and
release notes on 2026-10-05:

| Need | 26.8 status | How the design uses it |
|---|---|---|
| Tenant in the token | Organizations: supported; `scope=organization:<alias>` | tenant = the token's organization |
| Per-plugin audience | RFC 8707 `resource`: **experimental**. Token exchange V2 `audience`: **GA** | the api exchanges the user's token for a token with that plugin's audience, only when the plugin is in use for the tenant (our store decides). The web's own token never carries a plugin audience. |
| Operator acting as tenant | delegation with `act`: **preview**; impersonation: legacy V1 only (deprecated) | not used: an operator who is a member of the tenant's organization requests `organization:<alias>` (5s) |
| Provisioning | SCIM 2.0: **supported**, native | users and groups from the customer's directory |
| Policy decisions | AuthZEN: **experimental**; Authorization Services: supported | roles in the token; no AuthZEN |
| MCP authorization | spec 2025-03-26 supported; 2026-07-28 experimental | our MCP servers are resource servers that validate the exchanged token and serve RFC 9728 metadata; the api is a confidential client, so it needs neither DCR nor CIMD |

**There is no LTS in community Keycloak.** Only the newest minor gets patches. Long-term support exists only in the Red
Hat build of Keycloak (RHBK), whose 26.x stream is supported for at least two years.

For dev and qa, `dev-login` is today's symmetric dev issuer (decided after the eighth audit). It keeps the stack and CI
free of a Keycloak service. Real Keycloak flows are tested against a Keycloak 26.8 Testcontainer with a fixture realm
export.

### 5s. Only supported Keycloak features in stage and prod (decided with the user, 2026-10-05)

stage and prod use only Keycloak features documented as supported. Preview and experimental features (RFC 8707
`resource`, token-exchange delegation with `act`, AuthZEN, CIMD, legacy token exchange V1) may be tried in dev and qa
only. A standard whose implementation is still preview is not yet established for us.

**What this changes:**
- **Per-plugin audience.** Token exchange V2 with `audience`, which is GA, replaces RFC 8707.
- **Operator acting as a tenant.**
  - Operators are members of the organizations they serve, holding the `platform_operator` role. They pick a tenant
    with `scope=organization:<alias>`, so the tenant still comes from the principal.
  - Break-glass content access (5n) is a grant in our store (reason, at most one hour, audited). The api checks it on
    every content route, and Keycloak needs no preview feature for it.

**Enforced by a test.** A check on the realm export used for stage and prod fails if any preview or experimental
feature flag is enabled, naming the flag.

### 5n. Operators see a tenant's content only by break-glass (decided with the user, 2026-10-05)

An act-as-tenant token (5c) comes in two kinds:

- **Configuration**, the default. It can read and change the tenant's allowances and enablement, and read counts. It
  cannot read content, so every route that returns content refuses it. Content here means conversations, questions,
  answers, feedback on a turn, and the tenant's indexed documents.
- **Content access (break-glass).** The operator states a reason, such as a ticket reference, which is required and is
  never free-form content of the tenant. The grant lives in our store (5s), not as a token claim, and lasts at most one hour. It cannot
  be renewed, only issued again with a new reason.

Every break-glass grant is audited: who, which tenant, reason, start and end. The tenant sees it. The `tenant-admin` dashboard
has a section "Operator access to your content" that lists every grant, and the platform banner shows while one is
active.

The audit row is written whether or not `tenant-admin` is installed, so it is there whenever the tenant can look. What the
operator reads is never logged: the logs record that a read happened, never its content.

### Operator acting as a tenant (from 5c)

**The tenant still comes only from the principal.** The operator acts on a tenant only through a token issued for that
tenant: the operator's own token, requested with `scope=organization:<alias>` (Keycloak Organizations, supported). That
token carries the tenant as its organization, and the operator's own identity with the `platform_operator` role, so both
are in one principal and no `act` claim is needed (5s). So the operator routes are `/api/platform/plugins` (for the tenant in the token) and never `/tenants/{id}/…`. The web shows
plainly that an operator is acting as a tenant. Issuing such a token is itself audited.

### Operators and organization membership (review)

An operator must be a member of every organization they serve, with the `platform_operator` role. Membership is
granted by the platform's own provisioning, never by the tenant, and is listed in the platform dashboard.

Keycloak records an operator's sign-in into a tenant's organization in its own event log, which the tenant cannot see.
So the api writes its own audit row the first time an operator principal for a tenant reaches it in a session, and the
tenant dashboard lists those rows. The tenant sees that an operator entered, not only break-glass grants.

## Risks / Trade-offs

- [Community Keycloak has no LTS] → we follow the newest minor and pin the image in DECISIONS. If a support contract is
  needed, RHBK is the supported build of the same code.
- [Groups too many for a token] → resolve groups server-side from the IdP. The choice is recorded in DECISIONS when
  `document-acls` needs it.
