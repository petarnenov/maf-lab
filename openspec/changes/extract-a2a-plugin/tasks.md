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

- [x] 2.1 `A2A:Clients` keys and the compliance env folded into `a2a.env`; the store keyspace required.
- [x] 2.2 In one step: the tables (`IContributesModel`), the SQLite stores, the push dispatcher, the plugin's lb parts and
      open work, and the whole `/api/admin/a2a` endpoint (inbound, outbound, deliveries, cancel — the optional server
      lookup and the core's partner-accessor registration go) reading the audit through `IAuditTrail`, and the A2A
      admin page into the plugin's web part.
- [x] 2.3 `a2a-inspector` depends on `a2a` and `compliance`; the allow-list lines removed; DECISIONS §81 part K.
- [x] 2.4 Open the `generalize-a2a-skills` proposal stub.

## 3. Verify

- [x] 3.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 3.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.

Migration sequencing (owner, 2026-10-08): task 3.2 is final validation after **all** planned code migrations. Do not
start indexing, reindexing, graph refresh or index-backed live evals/CI during the code moves. Task 3.1 remains the
build, fixture-test, documentation and spec gate with the folder present and removed.

Batch 2 fixture evidence (2026-10-09): solution build with warnings as errors; full .NET 1939/1939 with the folder
present (together with coverage), 1883/1883 with it moved aside; vitest 702/702 present and 694/694 absent; web build
and lint; docs-check (130 Python tests) and all 63 strict spec validations. Final stack/index-backed CI remains 3.2.
