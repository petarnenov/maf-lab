# Tasks

The decision engine's question set narrows to the tenant's domains. That is the same request shape, so read
docs/rules/jev-usage.md, but no new question is added.

## 1. Store and gating

- [ ] 1.1 Allowance and enablement rows per tenant and plugin: atomic writes, a Redis-notified cache with a 30-second
      TTL, and a snapshot per turn. Verify with a two-replica test that a change on one replica is taken by the other,
      and that a dropped notification expires within the TTL.
- [ ] 1.2 `IPluginAccess.For(principal)`, read by the domain catalogue, the prompt, the decision engine's question set,
      tool selection and `/api/plugins`. Add a core filter on every plugin route group (404 when not in use). Verify the
      scenario "Two tenants, one installation".

## 2. Per-plugin audiences

- [ ] 2.1 Replace bearer forwarding (`ToolSource.cs`) with a token exchange per plugin (V2 `audience`), cached per user,
      plugin and expiry. The in-repo MCP servers validate the audience. Verify the scenario "A tenant calls a disabled
      plugin's server directly".

- [ ] 2.2 Keycloak setup in the realm exports:
  - the api client has "Standard token exchange" on, with client scopes `organization`, `domain-claims` and one
    audience mapper per plugin client;
  - each plugin server is a confidential OIDC client with no flow enabled (no standard flow, direct grants or
    service accounts), whose id is the audience;
  - the web client's tokens carry the api in `aud`.

  Verify with a Keycloak 26.8 Testcontainer the scenario "The exchanged token carries the tenant and the domain
  claims", that an exchange requested by any other client is refused, and that a subject token without the api in
  `aud` is refused.
- [ ] 2.3 `dev-login` mints a token for a persona and an audience (`audience` in `/dev/token`'s JSON body, and
      `make dev-token PERSONA= [TENANT=] AUDIENCE=`, whose output api-route terminal targets export as
      `MAF_BEARER_TOKEN`).
      `EvalAgentHost` mints one dev token per tenant and plugin audience the same way (no exchange, no Keycloak in CI).
      `mcp-inspector`'s `start.mjs` requests one token per listed server's audience on refresh. Verify that
      `make eval SUITE=selection` runs against audience-checking servers, for firm-a and firm-c.

## 3. Dashboards

- [ ] 3.1 `tenant-admin` and `platform-admin` shells with their section registries and the `PlatformAdmin` policy:
      plugin checkboxes, confirmation on unchecking and on withdrawing an allowance, and private plugins (`private_to`).
      Add `make tenant-allow TENANT= PLUGIN= [ENABLE=1]` through an operator token for the tenant's organization.
      Verify the dashboard and checkbox scenarios.
- [ ] 3.2 Dev and CI bootstrap:
  - `dev-login` gains an `operator` persona (`PLATFORM_ADMIN`, plus the requested tenant): `make dev-token
    PERSONA=operator TENANT= AUDIENCE=api`;
  - `make up`, `ci-e2e` and `evals.yml`, after the health wait and the lb reload, allow and enable every installed
    tenant-scoped plugin for firm-a, firm-b and firm-c (`shared` has no allowance row), idempotently, with one line
    per tenant and Ctrl+C between calls exiting 130. If the loop ever takes longer than 3 s in a terminal, it shows a determinate
    done/total bar instead (progress-feedback).

  Verify that a fresh `make` and `make eval SUITE=selection` (firm-a, firm-c) behave as before this change.
