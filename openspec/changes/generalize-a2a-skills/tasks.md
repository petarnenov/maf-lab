## 1. Design

- [ ] 1.1 Survey the skills seam and the scope question; record the rulings in this proposal.

## 2. Implement

- [ ] 2.1 The skills seam in the abstractions; the a2a plugin builds the card and dispatches through it.
- [ ] 2.2 Billing's skills into the billing plugin; the a2a plugin's `names a domain` fences removed.

## 3. Verify

- [ ] 3.1 `make test`, `make docs-check` and `openspec validate --strict` pass with each plugin present and moved aside.
- [ ] 3.2 `make ci-e2e` and `make ci-e2e-core` pass.
