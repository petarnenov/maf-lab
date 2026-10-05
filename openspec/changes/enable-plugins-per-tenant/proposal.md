# Proposal

## Why

After `introduce-plugins`, a plugin is installed for a whole deployment: an installed tenant-scoped plugin is in use for
every tenant. A multi-tenant enterprise assistant needs more than that:
- the platform decides what each tenant may use;
- each tenant decides what it uses;
- a tenant's users can never reach a plugin it does not use, not even by calling the plugin's server directly.

## What Changes

- **Allowed, then enabled.**
  - The operator allows installed tenant-scoped plugins per tenant.
  - The tenant's `TENANT_ADMIN` enables or disables plugins from that allowance.
  - Withdrawing an allowance disables the plugin as well.
- **One store, any replica.** One row per tenant and plugin, written atomically. Each replica keeps a cache dropped on
  a Redis notification and bounded by a 30-second TTL. A turn snapshots the set at its start.
- **Gating everywhere.** What reads the per-tenant set:
  - the domain catalogue and the prompt;
  - the decision engine's question set;
  - tool selection;
  - `/api/plugins`;
  - a core filter on every plugin route group, answering 404 for a plugin not in use.
- **At the plugin's own server.** The api no longer forwards the user's bearer. For each call it exchanges the user's
  token for one whose audience is that plugin alone (token exchange, RFC 8693, Keycloak's supported V2), and only when
  the plugin is in use for the tenant.
- **Private plugins** (5l): a tenant's own MCP server joins through the operator, marked `private_to`.
- **Two admin plugins** (5m): `tenant-admin` (the tenant's dashboard) and `platform-admin` (the operator's). Both are
  shells that other plugins add sections to.
- **Checkboxes, and a confirmation to switch off** (5p).
- **Disabling keeps the data.** Deleting it is a separate, confirmed action.

## Capabilities

### New Capabilities

- `tenant-plugins`: allowance and enablement per tenant, gating, per-plugin audiences, private plugins, admin
  dashboards, checkboxes and confirmation.

### Modified Capabilities

None. `plugins` (from `introduce-plugins`) is unchanged: "in use" gains a per-tenant meaning through `tenant-plugins`.

## Principles

- SOLID: the per-tenant set is one service, `IPluginAccess.For(principal)`. Every reader depends on it and never reads
  the store (dependency inversion). The admin shells are open for extension through their section registries and closed
  for modification (open/closed).
- Standards:
  - OAuth token exchange (RFC 8693) for per-plugin audiences;
  - MCP authorization (audience-bound tokens, no token passthrough);
  - cache-aside with TTL plus pub/sub invalidation;
  - feature toggles as permission toggles (Fowler).
- Own: the allowance and enablement rows and their routes. They are per-tenant entitlement data with no standard
  format. DECISIONS §84 (new, written when this change is applied).

## Progress

- Terminal: the dev and CI bootstrap in `make up` prints one plain line per fixture tenant ("firm-a: 5 plugins allowed
  and enabled"). Each call is sub-second, as are `make dev-token` and `make tenant-allow`.
- Page: none. Switching a checkbox is a single write, and the token exchange is one short call per plugin per turn.

## Stopping

- Key: Ctrl+C during the bootstrap in `make up`; Esc in the uncheck confirmation dialog
- Stop: Ctrl+C stops between `tenant-allow` calls, never inside one, and exits 130 with "rerun make up". That is safe
  because every call is idempotent. Esc in the dialog cancels before anything is written. A checkbox write is atomic.
- Recorded in: the allowance and enablement rows (each call's write is atomic)
- Shown: the bootstrap's last line names the tenants done; the dialog closes with the box unchanged

## Documentation impact

- `docs/http-api.md`: `/api/admin/plugins` and `/api/platform/plugins`.
- README: the tenant and platform dashboards.
- `docs/plugins.md`: scope `tenant` and what "in use" means.
- DECISIONS §84 (new): the per-tenant store, the TTL, and token exchange instead of bearer forwarding.
