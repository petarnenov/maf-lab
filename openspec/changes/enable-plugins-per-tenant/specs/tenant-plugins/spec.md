# Spec Delta

## ADDED Requirements

### Requirement: Enabled per tenant
A tenant-scoped plugin SHALL be in use for a request only when it is installed and enabled for the tenant of the
request's principal. The tenant SHALL never come from a parameter. A turn SHALL use the set as it was at the turn's
start. Changing the set SHALL be one atomic write in the shared store, taken by every replica without sticky routing,
and no replica SHALL use a stale set for longer than the cache TTL.

#### Scenario: Two tenants, one installation
- **WHEN** tenant A has `portfolio` enabled and tenant B does not
- **THEN** tenant A's turns may be routed to portfolio, and tenant B's turns are not offered it, see no portfolio card and
  no portfolio screen

#### Scenario: The exchanged token carries the tenant and the domain claims
- **WHEN** the api exchanges a tenant A user's token for the billing plugin's audience
- **THEN** the exchanged token's audience is the billing plugin alone, it carries organization A, and it carries the
  user's `domain_roles` and `advisor_ids`, and the billing server builds principal {tenant A, …} from it

#### Scenario: A tenant calls a disabled plugin's server directly
- **WHEN** a user of a tenant without `portfolio` sends their own token to `/portfolio/mcp`
- **THEN** the server rejects the token, because only a token exchanged for the portfolio audience is accepted, and the
  api exchanges one only for tenants that use it

### Requirement: The operator allows, the tenant enables
A platform operator (`PLATFORM_ADMIN`) SHALL decide which installed tenant-scoped plugins a tenant is allowed. The tenant's
`TENANT_ADMIN` SHALL enable or disable plugins for its own tenant from that allowance. Withdrawing an allowance SHALL also
disable the plugin for the tenant.

The operator SHALL act on a tenant only with a token issued for that tenant's organization. The token carries the tenant and
the operator's own identity with the operator role. Each such token and each change SHALL be audited without content.

#### Scenario: A tenant admin enables what is not allowed
- **WHEN** a `TENANT_ADMIN` tries to enable a plugin the operator has not allowed for the tenant
- **THEN** the change is refused and nothing is written

### Requirement: Disabling keeps the tenant's data
Disabling a plugin for a tenant SHALL stop its use and hide it, and SHALL keep the tenant's data in it. Deleting that data
SHALL be a separate admin action, confirmed in the UI.

#### Scenario: Enabled again
- **WHEN** a plugin is disabled for a tenant and later enabled again
- **THEN** the tenant's earlier data in it is there again

### Requirement: Admin dashboards are plugins that others extend
The tenant's administration SHALL be the `tenant-admin` plugin (tenant scope, every environment, for the tenant's
`TENANT_ADMIN`). The platform's administration SHALL be the `platform-admin` plugin (installation scope, every
environment, for principals with the `PLATFORM_ADMIN` role). Both SHALL be shells that other plugins add sections to
through the web registries `tenantAdminSections` and `platformAdminSections`. A section SHALL show only while its own
plugin is in use.

#### Scenario: A plugin's admin section
- **WHEN** `feedback-review` is enabled for a tenant and its `TENANT_ADMIN` opens the tenant dashboard
- **THEN** the dashboard shows the feedback review section, and for a tenant without the plugin it does not

#### Scenario: A tenant admin opens the platform dashboard
- **WHEN** a `TENANT_ADMIN` without the `PLATFORM_ADMIN` role requests `/api/platform/…`
- **THEN** the request is refused and nothing is returned

### Requirement: Plugins are switched with checkboxes, off only after confirmation
The tenant dashboard SHALL list the plugins allowed for the tenant as checkboxes, and the platform dashboard SHALL list a
tenant's allowable plugins the same way. Checking SHALL write at once. Unchecking SHALL first ask for confirmation in the
page's own dialog, saying what stops and that the data is kept, and SHALL write only on confirm. Installation-scoped
plugins SHALL have no checkbox. Installing SHALL NOT be possible from the web.

#### Scenario: Unchecking is cancelled
- **WHEN** a `TENANT_ADMIN` unchecks Billing and presses Esc in the confirmation dialog
- **THEN** nothing is written and Billing stays checked

#### Scenario: Unchecking is confirmed
- **WHEN** a `TENANT_ADMIN` unchecks Billing and confirms
- **THEN** Billing is disabled for the tenant from its next turn, and its data is kept

### Requirement: Per-tenant registration in the web
For a signed-in principal, `GET /api/plugins` SHALL list only the plugins in use for that principal's tenant: installed,
allowed and enabled. The web's registration rules, anonymous answer and error boundaries stay as `plugins` defines them.
A tenant-scoped plugin not in use for the tenant SHALL contribute no route, nav link, pane, card or admin section.

#### Scenario: Two tenants see different navigation
- **WHEN** a user of tenant A (portfolio enabled) and a user of tenant B (portfolio not enabled) sign in
- **THEN** only A's user sees portfolio's screens and cards, and B's user cannot open a portfolio route directly
