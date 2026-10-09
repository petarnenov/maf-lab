# Design

## Context

See proposal.md and the plugins delta. Coverage spans the API's EF model, background follower, AG-UI run projection,
repository writer, two remote hosts and the web. Its runs and refresh jobs describe one repository for the installation;
they retain that scope when moved. The core already exposes generic persistence, endpoint, audit and job seams.

## Goals / Non-Goals

**Goals:** Preserve existing state, routes, protocol framing, refresh and test-run lifecycles while making folder
removal eliminate the complete feature. Preserve the runner's internal network, absence of model secrets and unprivileged
execution. Give the plugin ownership of legacy coverage data backfills as well as table mappings.

**Non-Goals:** Change coverage algorithms, model prompts, task budgets or verification; run live test generation or
index-backed CI during migration. Those final checks follow all planned code moves, per the owner's migration order.

## Decisions

1. Keep `Maf.Lab.TestGen` as the shared coverage/test-generation contract library, beside Domain, Retrieval and A2A.
   The API-facing implementation becomes an in-process plugin using `IDbContextFactory<DbContext>` and `.Set<Row>()`.
   Existing table names, constraints and active-run index predicates remain explicit. Referencing the API assembly
   from the plugin would retain the extraction's reverse dependency.
2. Keep the protocol mappings in the core. A small Agent Framework content/input port lets the plugin read its run's
   thread and emit state and step content through those mappings. Extend the existing agent endpoint port with a thread
   guard so unknown runs retain their existing HTTP 404 before streaming. No AG-UI event is built by a plugin.
3. Add separate installation-job and system-audit ports for background repository work. Their core adapters preserve
   the existing `_repository` job scope and shared system audit identity; request-scoped tenant ports continue to
   derive identity from the validated token. Exposing the core runner or accepting arbitrary tenants would violate
   the plugin boundary and tenant rules.
4. Add a separate data-migration contribution invoked after the generic schema pass. The plugin owns its existing
   idempotent attempt/duration backfills; removing it leaves no SQL referring to its tables in the core. Model
   contribution alone cannot safely migrate historical row data.
5. Move both remote hosts into subdirectories of the plugin's `service/` and discover service projects recursively
   in the test-host glob. Keep their service namespaces and wire identities, rename their assembly projects inside the folder to avoid
   coupling historical source examples to a plugin project, use the existing service extern alias in tests, and
   update Dockerfiles and content-root lookup. This keeps their code out of the API and product images.
6. Move web components, DTOs, tests, compose services, balancer snippets, make targets and HTTP references together.
   Test fixtures explicitly install the plugin. Its AG-UI runtime agent is a manifest contribution; the core runtime
   keeps only chat. The installation's deployment configuration remains in compose env files.

## Risks / Trade-offs

- Existing SQLite rows or active-run uniqueness could change under new entity mappings → retain table/index names
  and backfill SQL; run storage and API tests with the plugin present and folder removal checks with it absent.
- A background follower could lose system identity or stop propagation → preserve identity in the core audit adapter,
  register the follower only with the plugin, and expose open work through the owning run/job stores.
- Model-written code could gain network access or secrets during the service move → preserve runner network and
  container restrictions verbatim; inspect merged compose configuration without launching index/eval work.
- The test host can accidentally read another service's copied appsettings → keep explicit content roots and service
  assembly aliases in plugin-owned tests.

## Migration Plan

Move code and deployment fragments as one working-tree change, then run builds, fixture tests, docs and strict spec
checks with the folder present and moved aside. Preserve stored state for rollback by reinstalling the previous plugin
build. Run full stack/index-backed validation after the complete code migration; archive only after its required gates.
