# Proposal

## Why

An enterprise assistant must be able to do three things:
- remove a user's data everywhere when they leave or ask (GDPR right to erasure);
- hand over and then delete all of a tenant's data when its contract ends;
- keep each tenant's data only as long as its policy says.

With plugins keeping data of their own, the core cannot do this alone (decision 5w, with the user, 2026-10-05).

## What Changes

- **`IContributesDataLifecycle`** is required of every plugin that keeps tenant or user data: export, delete per tenant,
  delete per user, apply retention. The core implements the same for its own data.
- **Orchestrated jobs** in the api's store, each showing progress and stoppable, with idempotent steps that resume:
  - user deletion, from an IdP reconcile or an erasure request;
  - tenant export and offboarding;
  - scheduled retention per tenant.
- **Audit records outlive an erasure** without content, naming the user by a pseudonymous id.
- **The contract suite** proves that deletion is complete for every plugin.

## Capabilities

### New Capabilities

- `data-lifecycle`: export, deletion per user and per tenant, retention per tenant, across the core and every plugin.

### Modified Capabilities

None.

## Principles

- SOLID: each store owns its own deletion (single responsibility), behind one small interface (interface segregation).
  The core orchestrates through the abstraction (dependency inversion).
- Standards:
  - Law and records: GDPR Art. 17 (erasure, with the Art. 17(3)(b) exception for records kept by legal obligation)
    and Art. 20 (portability); ISO 15489 records schedules; legal hold.
  - Identity: SCIM 2.0 for provisioning; the Keycloak Admin REST API for reconciliation.
  - Job design: idempotent, resumable jobs.
- Own: the export bundle's layout (one JSON Lines file per store, plus a manifest). It is a common shape, but no single
  standard governs a multi-store export. DECISIONS §86 (new, written when this change is applied).

## Progress

- Page: lifecycle jobs show themed progress in the tenant and platform dashboards: stores done/total, the current
  plugin, and the counts deleted.
- Terminal: `make tenant-export TENANT=` and `make tenant-offboard TENANT=` show the same as a bar.

## Stopping

- Key: Esc on the dashboard page running a lifecycle job; Ctrl+C in the terminal targets
- Stop: the job's cancel route marks its row `canceled` atomically. The worker stops after the current plugin's step,
  never inside one. Ctrl+C in a terminal target calls that route, then exits 130.
- Recorded in: the job's row in the api's store, with each finished store listed
- Shown: "Stopping…" until the row says canceled, then what was deleted and what remains, with how to resume

## Documentation impact

- `docs/plugins.md`: the lifecycle contract.
- `docs/http-api.md`: the job routes.
- README: the targets.
- DECISIONS §86 (new): reconcile through the Admin REST API instead of an SPI, and the export bundle.
