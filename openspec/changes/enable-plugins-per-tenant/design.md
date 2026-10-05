# Design

## Context

Decisions taken with the user on 2026-10-05, moved here from `introduce-plugins`. Their letters are kept. This change
needs `introduce-plugins` (the manifests and their `scope`) and `adopt-company-idp` (real principals, the operator
role, the organization of a token).

### 5b. Plugins are enabled per tenant (decided with the user, 2026-10-05)

There are two layers:

- **Installed:** the deployment's set. `MAF_PLUGINS` and make decide which services run, which routes the lb has and
  which modules the api registers.
- **Enabled:** a per-tenant set, decided on every request from the principal's tenant. It never comes from a parameter.

A plugin must be installed before it can be enabled for any tenant.

**Scope.** Each manifest declares its scope:
- `scope = "tenant"` for anything that touches a tenant's conversation or data and that a tenant may choose: domains, cards, feedback review;
- `scope = "installation"` for tools of the platform itself: inspectors, observability, coverage and test generation
  over the repository, topology, the monitor and insights (dev tools, `introduce-plugins` 5d and 5i), and
  deployment-wide user features a tenant does not choose (`conversation-history`, `introduce-plugins` 5y).

An installation plugin is on for the whole deployment, or off.

**Store.** The per-tenant set lives in the api's shared store, as a row per tenant and plugin. A change is one atomic write,
so any replica takes it. Each replica keeps a short cache that it drops on a Redis notification. The cache also has a TTL of 30 seconds, so a
missed notification can never keep a disabled plugin in use for longer than that (review, minor). A turn snapshots the set
once at its start, so a change mid-turn never splits a turn.

**What reads the set:**
- `IDomainCatalogue.For(principal)`;
- tool selection;
- the decision engine's routing sets (a disabled domain is not an option);
- `/api/plugins`, which the web reads per signed-in user;
- every plugin endpoint, through a filter the core applies to all routes a plugin maps. A plugin cannot forget it.

**At the remote server, by the protocol's own means.** A remote plugin's route is reachable through the lb by anyone
with a token, so core-side gating is not enough. The user's own token carries only the api's audience. For each call to
a plugin's server, the api exchanges it (OAuth token exchange, RFC 8693, Keycloak's supported V2) for a token whose
audience is that plugin alone. It does so only when the plugin is in use for the principal's tenant, which our store
decides (`adopt-company-idp`, 5r and 5s). A conforming MCP server already rejects a token not meant for it. This
replaces today's forwarding of the user's bearer (`ToolSource.cs:64-66`). An A2A agent states
the same requirement through the security scheme in its card.

**Disabling a plugin for a tenant** hides it and stops its use. The tenant's data in that plugin is kept, and enabling it
again brings the data back. Deleting the data is a separate, explicit admin action, confirmed in the UI like every
write.

### 5c. Who switches a plugin for a tenant (decided with the user, 2026-10-05)

Two roles act, at two levels:

- **The platform operator** (`PLATFORM_ADMIN`, a new role) decides which installed tenant-scoped plugins a tenant is
  *allowed*.
- **The tenant's `TENANT_ADMIN`** enables or disables plugins for its own tenant, from what is allowed.

When an allowance is withdrawn, the plugin is disabled for that tenant as well. Both changes are one atomic write each,
and both are audited with who, when and what, never with content.

How the operator acts on a tenant is in `adopt-company-idp`.

### 5l. A tenant's own MCP server joins through the operator (decided with the user, 2026-10-05)

A tenant that has its own MCP server asks the operator. The operator adds a remote plugin for it to this repository and
reviews it like any other:
- the endpoint;
- the domain;
- the routing questions;
- the prompt fragment;
- the guard context.

The manifest marks the plugin `private_to = "<tenant_id>"`. The operator screen offers it to that tenant only, and the api
refuses to allow it for any other.

A tenant admin never enters a URL, a routing question or prompt text, because a tenant must not shape a Jev decision or the
system prompt. The api only calls endpoints named in reviewed manifests, which closes the SSRF path.

**The user's token is never forwarded** to a server outside the system (MCP forbids token passthrough). For each call
the api uses a token whose audience is that server alone, obtained by token exchange (RFC 8693). That token comes from
the IdP, which the tenant's server trusts through its published keys, or from the tenant's own authorization server through the MCP authorization
flow (protected resource metadata, RFC 9728). Either way, only the official protocol is used.

### 5m. Two admin plugins: the tenant's dashboard and the platform's (requested by the user, 2026-10-05)

| Plugin | Scope | Environments | Who sees it |
|---|---|---|---|
| `tenant-admin` | tenant | all | the tenant's `TENANT_ADMIN`, for their own tenant only |
| `platform-admin` | installation | all | the platform operators: users the identity provider puts in the operator group, which reaches the token as the `PLATFORM_ADMIN` role |

**Both are shells.** Each is a dashboard with an empty grid and its own navigation. Other plugins contribute to them
through two web registries, `tenantAdminSections` and `platformAdminSections`, and their api routes go behind the
matching policy. Neither shell knows which plugins exist. A contributed section shows only when its own plugin is in
use, for the tenant or for the installation.

**Tenant admin** (`/admin`). It holds:
- the tenant's plugins: which are allowed and which are enabled, with the switch (5c);
- `feedback-review` as a section;
- the tenant's own usage: turns, active users, domains used. These are counts from the core turn record, never content
  and never Jev internals (5i).

**Platform admin** (`/platform`). It holds:
- tenants and their allowances;
- private plugins (5l);
- every installed plugin with its health and environment;
- the audit log of operator actions;
- acting as a tenant (5c), shown in a banner on every page while active;
- `index-admin`, and `observability`'s telemetry screen when installed, as sections.

**Policies and routes.** `TenantAdmin` already exists. `PlatformAdmin` is a new policy on the role. Platform routes live
under `/api/platform/…` and never take a tenant parameter: a tenant-specific action uses the act-as-tenant token.

**Without the shells.** Without `tenant-admin`, a tenant keeps the plugins the operator enabled for it and has no screen to
change them. Without `platform-admin`, allowances are set from the terminal with `make tenant-allow TENANT= PLUGIN=`. It does not write the store
directly. It obtains an operator token acting as that tenant and calls the same `/api/platform/plugins` route, so the
tenant still comes only from a principal and the change is audited the same way. A fresh installation is bootstrapped that way.

**The dev and CI bootstrap (ninth audit 3).** `dev-login` gains an `operator` persona. It carries `PLATFORM_ADMIN` and
the tenant it is asked for, and is the dev stand-in for `scope=organization:<alias>`:
`make dev-token PERSONA=operator TENANT=firm-a AUDIENCE=api`. `make up`, `ci-e2e` and `evals.yml` then bootstrap the
fixture tenants (firm-a, firm-b, firm-c; `shared` is a corpus every tenant reads, not a tenant, so it has no
allowance row). After `wait_healthy.sh` and the lb reload, because the calls go through the lb, they run for each
`make tenant-allow TENANT= PLUGIN= ENABLE=1`, which allows every installed tenant-scoped plugin and enables it. So
today's behaviour, where every tenant uses every domain, holds in dev and CI. Each call is idempotent, so a second
`make up` changes nothing.

### 5p. Checkboxes, and a confirmation to switch off (decided with the user, 2026-10-05)

**Who uses which list:**
- In the platform dashboard, the operator allows plugins for a tenant with a list of checkboxes, one per installed
  tenant-scoped plugin.
- In the tenant dashboard, `TENANT_ADMIN` enables plugins with the same kind of list. It shows only the plugins allowed for
  the tenant: name, description, a "private" mark for 5l, and "unavailable right now" when the plugin's service is
  unhealthy.

**Installation-scoped plugins** have no checkbox anywhere. The platform dashboard lists them read-only with health and
environment.

**Checking and unchecking:**
- Checking writes at once, with no dialog.
- Unchecking asks first, in the page's own dialog design. For example: "The assistant stops answering about billing for
  the whole tenant. Its data is kept." Only Confirm writes. Esc or Cancel leaves the box checked and writes nothing.

The same dialog guards withdrawing an allowance in the platform dashboard. It also says that the plugin will be switched
off for the tenant.

**No installing from the UI.** Installation stays in the terminal (`make plugin-on`). Starting containers from a page
would give the api access to Docker, which amounts to root on the host, inside the process that holds the tenant
boundary.

### What the exchanged token must carry (re-review 9, corrected by re-audit 4)

How Keycloak's **standard token exchange (V2)** works, per its documentation:
- it is enabled by the "Standard token exchange" switch on the **requesting** client, which is the api. No
  fine-grained admin permission is involved;
- the subject token must already name the requester in `aud`;
- the exchanged token's claims come from the **requester's** effective client scopes;
- `audience` only narrows them.

The setup therefore lives on the api's client and the web's client, not on each plugin:
- **The web client's tokens carry the api in `aud`**, through an audience mapper (set up in `adopt-company-idp`).
- **The api client has "Standard token exchange" on.** Its client scopes carry:
  - `organization`, so the tenant survives;
  - `domain-claims`, whose mappers copy `domain_roles` and `advisor_ids`;
  - one audience mapper per plugin client, so each plugin can be requested as an `audience`.
- **Each plugin server is a confidential OIDC client with no flow enabled** (no standard flow, direct grants or
  service accounts), whose client id is its audience. "Bearer-only" is deprecated since Keycloak 26.7 and gone from the
  admin console, so it is not used. A plugin server accepts only tokens with
  its own id in `aud`.
- **The setup lives in each environment's realm export** (and in the Testcontainers fixture realm), so it is reviewed
  and checked like code.

If an exchanged token lacked the organization, the plugin's server would have no tenant and would refuse the call.
It never falls back to a default.

**Dev and qa tools that call MCP servers directly (seventh audit F1).** Once a server accepts only its own audience,
a single token can serve only one server.
- **`make eval`** hosts `ChatTurnRunner` in process (`EvalAgentHost.cs:158-171`) and reaches three servers. As
  `mcp-inspector` does, it mints one dev token per tenant and plugin audience from `dev-login`'s `/dev/token` with `audience` in its JSON body, beside the tenant id.
  No token comes from the environment, and CI needs no Keycloak. The exchange itself is proven by the Testcontainers
  test of task 2.2.
- **`mcp-inspector`** lists three servers (`start.mjs:15`). On its hourly refresh it requests one token per listed
  server's audience from `dev-login`'s `/dev/token`, with `audience` in its JSON body.
- **`MAF_BEARER_TOKEN`**, the output of `make dev-token PERSONA=operator TENANT= AUDIENCE=api`, is only for terminal targets that call api
  routes (`data-lifecycle`).

All of these are dev and qa plugins, absent from stage and prod, where only the api exchanges tokens.

## Risks / Trade-offs

- [The token exchange adds a call per plugin per turn] → exchanged tokens are cached per user, plugin and expiry, and
  the exchange is one round-trip to the IdP.
- [A stale cache after a missed notification] → bounded by the 30-second TTL.
