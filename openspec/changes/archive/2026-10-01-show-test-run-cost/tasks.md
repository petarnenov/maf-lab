# Tasks

## 1. Api: cost in the overview

- [x] 1.1 Add `Tokens`, `CostUsd`, `CostIsEstimate` and `Budget` to `TestAgentRun`, filled in `TestAgentOverview.Recent` from the row and the configured allowlist (a model not on it reads as an estimate); verify with xUnit tests in `RunCostTests` (finished, running so far, nothing spent, zero-priced model, estimate vs list price, model gone from the allowlist, recorded cost kept when the price changes) and `TestAgentOverviewApiTests` (the fields over HTTP), and that `make lint-dotnet` is clean

## 2. Web: Cost column

- [x] 2.1 Add `cost(usd, estimate)` to `web/src/coverage/format.ts` (`$0.042`, `$1.27`, `<$0.001`, `$0.00`, `—`, `≈` for an estimate) and the four fields to `TestAgentRun` in `web/src/api/types.ts`; verify with Vitest cases for each form
- [x] 2.2 Add the Cost column after Duration in `TestAgentSection`, with "so far" for a running run and a tooltip with tokens, estimate and cost cap; verify with Vitest cases (finished with cap and estimate, running, zero, a row without the fields) and that the existing section tests pass; `make test-web`, `make lint-web` and `make build-web` pass

## 3. Documentation

- [x] 3.1 Update the `test-agent` paragraph of `docs/http-api.md` (the new `recent` fields, which price `costUsd` uses, what `costIsEstimate` means) and the `/admin/a2a` paragraph of `README.md`; add DECISIONS §73; run `make docs` and `make docs-check`; both succeed
