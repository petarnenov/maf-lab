## 1. Move `a2a` into `plugins/a2a/`

- [ ] 1.1 Create `plugins/a2a/` with the card, handler, partner-token routes; rename the Billing* files.
- [ ] 1.2 Derive the card's skills and tags from the catalogue; add `depends = ["a2a"]` to `a2a-inspector`.
- [ ] 1.3 Remove the allow-list line; update the docs.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
