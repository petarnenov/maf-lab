## 1. Move `feedback-review` into `plugins/feedback-review/`

- [x] 1.1 Create `plugins/feedback-review/` with the queue, labelling and routes.
- [x] 1.2 Remove the allow-list line; update the docs.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.

Migration sequencing (owner, 2026-10-08): task 2.2 follows all planned code migrations; no intermediate indexing or live evals.
