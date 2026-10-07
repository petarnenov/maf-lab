## 1. Correct the spec

- [x] 1.1 The tenant-isolation delta: "Valid token yields principal" states that domain roles and advisor ids travel in
      the token for the domain's server, which none reads today.
- [x] 1.2 DECISIONS §86: billing advisor scoping is not implemented; a future billing-plugin change.
- [x] 1.3 `openspec validate --strict`, `make specs` and `make docs-check` pass.
