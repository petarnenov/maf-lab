## 1. Move `evals` into `plugins/evals/`

- [x] 1.1 Create `plugins/evals/` with the screen, routes, `make eval*`, `DatasetWriter` and the A2A probe.
- [x] 1.2 Host the code server through the catalogue's endpoint; move `src/Maf.Lab.CodeSearch`, the graph-tool tests and the code graph builder under the code plugin.
- [x] 1.3 Update the docs.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.

Migration sequencing (owner, 2026-10-08): task 2.2 is final validation after **all** planned code migrations.
Do not start indexing, reindexing, graph refresh or index-backed live evals/CI during the code moves.
