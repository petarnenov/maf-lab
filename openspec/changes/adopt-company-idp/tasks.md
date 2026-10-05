# Tasks

No decision-engine call is added or changed, so the Jev review checklist does not apply.

## 1. Sign-in

- [ ] 1.1 OIDC sign-in in the web (authorization code + PKCE). The api and the in-repo MCP servers validate the IdP's
      JWKS-signed tokens. The principal comes from the organization, roles and standard claims, and gains
      `GroupIds`. The web client's tokens carry the api client in `aud` (an audience mapper). Verify with a Keycloak
      26.8 Testcontainer and a fixture realm that a user of organization A gets principal {tenant A, role, groups}, and
      that a token from another issuer is refused.
- [ ] 1.2 `dev-login` plugin (installation scope, dev and qa only): today's symmetric dev issuer, unchanged in kind. No
      Keycloak service in compose or CI. Verify that `MAF_ENV=stage` refuses it.

## 2. Operator and break-glass

- [ ] 2.1 `PLATFORM_ADMIN` and `platform_operator`. An operator acts on a tenant only through
      `scope=organization:<alias>`, and no route takes a tenant parameter. Verify that a request without the role is
      refused. The api mirrors an operator's first request per session into the tenant-visible audit; verify that the
      tenant's dashboard lists it.
- [ ] 2.2 Break-glass grant in our store (reason, at most one hour, audited, listed for the tenant). Content routes
      refuse an operator without an active grant. Verify the scenarios "Reading a conversation without a grant" and
      "The tenant sees the access".

## 3. Supported features only

- [ ] 3.1 A check on the stage and prod realm export that fails on any preview or experimental feature flag, naming it.
      Verify that it fails on a planted `token-exchange-delegation`.

## 4. Documents

- [ ] 4.1 CLAUDE.md, DECISIONS §83 (Keycloak pin, supported-features rule, break-glass), http-api.md and the README.
