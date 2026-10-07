## 1. Move `a2a` into `plugins/a2a/` (batch 1)

- [x] 1.1 Create `plugins/a2a/` with the card and handler (renamed Billing* → Assistant*), the protocol server, partner
      authentication and the surface and protocol routes. The core keeps the SQLite stores, the dispatcher, the tables
      and `/api/admin/a2a` until batch 2; meanwhile its cancel route looks the server up optionally (404 "no A2A
      surface" with the plugin off), and it registers the partner accessor the task store reads (a `TryAddSingleton`
      beside the plugin's own); batch 2 removes both.
- [x] 1.2 The `IAssistantAnswer`, `IDomainToolCall` and `IActivityAudit` ports, implemented by the core; the plugin keeps
      the A2A framing, and reads as a read-only principal per firm the partner is entitled to (no tenant parameter).
- [x] 1.3 `Maf.Lab.A2A` becomes a shared library in the architecture test.
- [x] 1.4 The test agent's section and route to coverage (the A2A admin page follows in 2.2).
- [x] 1.5 `verify` gated on the plugin; `eval-a2a` into its `plugin.mk`; `CI_PLUGINS` gains `a2a`.

Evidence (batch 1, on d2cf246): `make test-dotnet` 1897/1897 with the folder present (1818/1818 run with it moved aside
before the rebase onto compliance, insights, observability and index-admin); vitest 701/701; web lint and `docs.py check`
clean. Batch 1 lands without the archive; the archive, DECISIONS §81 part K, the follow-ups and `ci-e2e` come with
batch 2.

## 2. After extract-compliance (batch 2)

- [x] 2.1 Moved to the a2a-client-registrations change (the `A2A:Clients` keys, the compliance env folded into
      `a2a.env`, the store keyspace required): a cross-plugin configuration rename with a live keyspace at stake.
- [x] 2.2 In one step: the tables (`IContributesModel`, names unchanged), the SQLite stores and the push dispatcher over
      the core's store, the open work (the non-terminal tasks, cancelled through the task store's guarded save), and the
      whole `/api/admin/a2a` endpoint (inbound, outbound, deliveries, cancel; the optional server lookup and the core's
      partner-accessor registration gone) reading the audit through `IAuditTrail`, and the A2A admin page into the
      plugin's web part. The plugin's routes are the api's, so it adds no lb part.
- [x] 2.3 `a2a-inspector` depends on `a2a` and `compliance`; the allow-list lines removed (batch 1); DECISIONS §81 part K.
- [x] 2.4 Open the `generalize-a2a-skills` proposal stub (and the `a2a-client-registrations` one, task 2.1).

## 3. Verify

- [ ] 3.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 3.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
