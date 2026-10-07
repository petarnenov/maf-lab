## 1. Registrations

- [ ] 1.1 `A2A:Clients:<agent>` bound by the consultant, the topology probe and the eval host, with `Compliance:*` as a
      one-release fallback; `compose/env/compliance.env` folded into `compose/env/a2a.env`.
- [ ] 1.2 `A2AOptions.StoreKeyspace` required; `compliance` and `testgen` set explicitly where they are used today.
- [ ] 1.3 The test agent's client keys to `A2A:Clients:test-agent`.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass.
- [ ] 2.2 `make ci-e2e` and `make ci-e2e-core` pass; the reviewer's existing Redis tasks are still read after the change.
