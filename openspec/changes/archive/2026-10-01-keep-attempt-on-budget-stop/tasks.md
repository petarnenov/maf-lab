# Tasks

## 1. Api

- [x] 1.1 Keep the started attempt when applying the final report in `TestGenRuns.ApplyAsync`; verify with an api test where the budget is spent during attempt 1 (run ends with attempt 1, reason `budget`)
- [x] 1.2 Add the idempotent backfill of `Attempt` from the run's activity; verify with a storage test (a row at 0 with activity in attempt 1 reads 1; a correct row is unchanged)

## 2. Verification

- [x] 2.1 Run `make test` and `make lint`; both pass
- [x] 2.2 After `make`, the stored run `r_631320cb…` shows attempt 1

## 3. Documentation

- [x] 3.1 Confirm no document names the budget-stop attempt count (proposal: none affected), then run `make docs` and `make docs-check`; both succeed
