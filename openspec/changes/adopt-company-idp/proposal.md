# Proposal

## Why

Sign-in today is the lab's dev issuer: `DevTokenPicker` and a symmetric dev JWT that the api and the MCP servers
validate (`src/Maf.Lab.Retrieval/Auth/DevJwt.cs`). An enterprise assistant signs users in with their organisation's
identity. The company is building its own identity provider on Keycloak, and authentication and authorization will come
from it (decided with the user on 2026-10-05).

The later changes depend on this one: per-tenant plugins, document permissions and the data lifecycle all need real
principals, groups and an operator role.

## What Changes

- **Sign-in through the company IdP.**
  - The web signs in with OIDC (authorization code + PKCE) against Keycloak 26.8.
  - The api and every in-repository MCP server validate the IdP's tokens through its published keys (JWKS), instead of
    the symmetric dev key.
- **The principal comes from the IdP's token:**
  - the tenant from the Keycloak organization (`scope=organization`);
  - roles from realm or client roles;
  - user id and group ids from standard claims.

  The tenant is still never taken from a request.
- **`PLATFORM_ADMIN`.** The platform operator is a new role. An operator acts on a tenant only with a token requested
  for that tenant's organization (`scope=organization:<alias>`), as a member of it holding `platform_operator`. There
  is never a tenant parameter.
- **Break-glass (design 5n).** By default an operator's token for a tenant grants configuration and counts only. Access
  to a tenant's content requires a grant in our store: a reason, at most one hour, audited, and visible to the tenant.
- **Only supported Keycloak features in stage and prod (design 5s).** A check on the stage and prod realm export fails
  on any preview or experimental feature flag.
- **The dev issuer becomes the `dev-login` plugin** (installation-scoped, dev and qa only). `dev-login` is today's
  symmetric dev issuer, kept as it is, and is not a Keycloak. The stack and CI run no Keycloak. Real Keycloak
  behaviour is tested with Testcontainers (tasks 1.1 here, 2.2 in `enable-plugins-per-tenant`).
- **The api still forwards the user's token to MCP servers in this change**, as today. Exchanging it for a token per
  plugin audience comes with `enable-plugins-per-tenant`, which needs the per-tenant store. The web client's tokens
  already carry the api in `aud`, which that exchange will require.

## Capabilities

### New Capabilities

- `identity`: sign-in through the company IdP, the operator role and acting as a tenant, break-glass content access,
  and the supported-features rule.

### Modified Capabilities

- `tenant-isolation`: the principal comes from the company IdP's token (the tenant from its organization), not from the
  local dev issuer. The dev issuer remains only in dev and qa.

## Principles

- SOLID: identity sits behind ASP.NET Core's authentication handlers (dependency inversion). The core reads a
  `Principal` and never a token format.
- Standards:
  - Protocols and tokens: OpenID Connect (authorization code + PKCE), JWKS, OAuth 2.0 (RFC 6749), JWT (RFC 7519).
  - Keycloak: Organizations, and only its supported features.
  - MCP authorization with the MCP servers as resource servers, serving protected resource metadata (RFC 9728).
  - Access patterns: least privilege; break-glass access with audit.
- Own: the break-glass grant record (reason, expiry, audit) in our store. Keycloak's delegation with an `act` claim is
  still preview (design 5s), so no supported standard fits. DECISIONS §83 (new, written when this change is applied).

## Progress

- Terminal: none for the stack, which runs no Keycloak in dev, qa or CI. The company IdP is external. The realm-export
  check is a short, read-only pass.
- Page: sign-in is a redirect to the IdP and back, which needs no progress of its own.

## Stopping

- Key: Esc on the platform dashboard's break-glass dialog before it is confirmed
- Stop: Esc cancels the dialog and writes nothing. A grant already given ends at its expiry, or at once when the
  operator ends it, through its own route.
- Recorded in: the break-glass grant row in the api's store, whose end time is written atomically
- Shown: the banner disappears only once the grant row says it has ended

## Documentation impact

- `CLAUDE.md`: the principal comes from the company IdP. `dev-login` is dev and qa only. The Keycloak image is pinned.
- DECISIONS.md:
  - §83: Keycloak 26.8.0 pinned (a new image, which moves a package version);
  - the supported-features rule;
  - break-glass in our store instead of a preview `act` claim.
- `docs/http-api.md`: the operator and break-glass routes. README: sign-in.
