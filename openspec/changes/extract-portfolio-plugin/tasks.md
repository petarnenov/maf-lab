## 1. Move `portfolio` into `plugins/portfolio/`

- [ ] 1.1 Create `plugins/portfolio/` with its manifest, prompt, `server.json`, compose, lb parts and corpus.
- [ ] 1.2 Delete `Agent__Servers__portfolio__*` and the MCP Inspector's `builtIn` constant.
- [ ] 1.3 Remove `Agent__BuiltInDomains` (compose, `make core`) and `BuiltInDomains.Ids/Descriptors/Behaviours/AddStores`.
- [ ] 1.4 Update the docs and DECISIONS §81.

## 2. Verify

- [ ] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.
