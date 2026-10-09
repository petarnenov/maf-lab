## 1. Move `coverage` into `plugins/coverage/`

- [x] 1.1 Create `plugins/coverage/` with its services, page, A2A admin, tables and routes.
- [x] 1.2 Update the docs.

## 2. Verify

- [x] 2.1 `make test`, `make docs-check` and `openspec validate --strict` pass with the folder present, and again with
      it moved aside.
- [ ] 2.2 `make ci-e2e` (every plugin) and `make ci-e2e-core` (the core alone) pass.

Migration sequencing (owner, 2026-10-08): task 2.2 is final validation after **all** planned code migrations.
Do not start indexing, reindexing, graph refresh or index-backed live evals/CI during the code moves.

Fixture evidence (2026-10-09): warnings-as-errors build; .NET 1939/1939 with the folder present and 1618/1618 with
it moved aside (including the five new absence checks); vitest 702/702 present and 477/477 absent; web build/lint;
docs-check with 130 Python tests and 63 strict spec validations in both configurations. Final stack CI remains 2.2.
