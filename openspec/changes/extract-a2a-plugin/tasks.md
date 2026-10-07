## 1. Move `a2a` into `plugins/a2a/` (batch 1)

- [ ] 1.1 Create `plugins/a2a/` with the card, handler, stores, dispatcher, tables, partner auth, routes, lb parts and
      open work; rename the Billing* files to Assistant*.
- [ ] 1.2 The `IAssistantAnswer` port, implemented by the core; the plugin keeps the A2A framing.
- [ ] 1.3 `Maf.Lab.A2A` becomes a shared library in the architecture test.
- [ ] 1.4 The A2A admin page into the plugin's web part; the test agent's section and route to coverage.
- [ ] 1.5 `verify` gated on the plugin; `eval-a2a` into its `plugin.mk`; `CI_PLUGINS` gains `a2a`.

## 2. After extract-compliance (batch 2)

- [ ] 2.1 `A2A:Clients` keys and the compliance env folded into `a2a.env`; the store keyspace required.
- [ ] 2.2 The outbound half of the admin page through `IAuditTrail`.
- [ ] 2.3 `a2a-inspector` depends on `a2a` and `compliance`; the allow-list lines removed; DECISIONS §81 part K.
- [ ] 2.4 Open the `generalize-a2a-skills` proposal stub.

## 3. Verify

- [ ] 3.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 3.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
