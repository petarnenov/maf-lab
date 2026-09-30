# Tasks

## 1. Contract and estimate (`Maf.Lab.TestGen`)

- [ ] 1.1 Add `RunLimits` (attempts 1–10/10, tool rounds 1–40/40, test runs 0–2/2) with `LimitBounds`, keep `TestGenRequest.AttemptLimit` as an alias; verify with an xUnit test of the bounds
- [ ] 1.2 Add optional `ToolRoundsPerAttempt` and `TestRunsPerAttempt` to `TestGenRequest`, check them in `Problem()`, expose resolved values; verify with xUnit cases (41 rounds rejected, absent → 40/2, 0 test runs accepted)
- [ ] 1.3 Replace `AttemptEstimate` constants with the fitted formula (43 000 + 4 × fileTokens + 4 000 × min(rounds, 20) in, 6 000 out); verify the pinned example (3 745 bytes, glm prices, 20 rounds → $0.089 per attempt) in xUnit

## 2. Test agent

- [ ] 2.1 Remove `MaxToolRoundsPerAttempt` / `MaxTestRunsPerAttempt` from the agent options and read the request's limits in the handler, nudge, tool loop, instructions and `TestAgentTools`; make the `run_tests` description defer to the instructions; verify with agent tests (cap from task stated in the attempt input, 0 test runs refused)
- [ ] 2.2 Use the task's round limit in the budget pre-check (`PerAttempt`); verify the existing budget-stop tests still pass

## 3. Api

- [ ] 3.1 Add `Limits` to `StartRunRequest`; resolve, validate (`400` field `limits`), store the new nullable columns on the run row and pass them to the task; verify with api tests (start with limits, without, out of bounds leaves threshold unchanged)
- [ ] 3.2 Add `Limits` to `RunSummary` (null columns read as defaults); verify a summary test
- [ ] 3.3 Change `GET /api/coverage/models` to return `limits` (min/max/default, deadline minutes, max suspected bugs) and the file's `estimate` parts, dropping `maxAttempts` and per-model `estimate`; update `CostEstimator`; verify with the picker endpoint tests

## 4. Web

- [ ] 4.1 Update `web/src/api/types.ts` for the new models response, start body and run summary limits; verify `npm run typecheck` (via `make lint`)
- [ ] 4.2 Add `web/src/coverage/limits.ts` (parse and bound-check the three limits, estimate from served parts) with Vitest tests, including the same pinned example ($0.089 per attempt, $0.89 for 10)
- [ ] 4.3 Extend `budget.ts` with per-cap `unlimited` flags (default checked) and prefill from the estimate on uncheck; update `budget.test.ts`
- [ ] 4.4 Rework `ModelPicker` into Model / Limits / Budget with defaults filled, read-only target, deadline and suspected-bug limit, and the live estimate; wire the dialog's Start to all validity checks and send `limits`; verify with a Vitest picker test (defaults shown, invalid bound disables Start, estimate follows attempts)

## 5. Verification

- [ ] 5.1 Run `make test` and `make lint`; both pass
- [ ] 5.2 Rebuild the stack (`make`) and open the "Reach N%?" dialog on a file at http://localhost:7171: every limit is shown with its default and the estimate for glm on a ~3.7 KB file reads about $0.89 for 10 attempts

## 6. Documentation

- [ ] 6.1 Update `docs/http-api.md` for `/api/coverage/models` and `/api/coverage/runs` (body `limits`, run `limits`)
- [ ] 6.2 Add a DECISIONS.md entry: per-run limits from the task and `RunLimits`, superseding §60's single location, and the estimator's constants with the measured runs they were fitted to
- [ ] 6.3 Run `make docs` and `make docs-check`; both succeed
