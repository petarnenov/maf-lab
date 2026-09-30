# Tasks

## 1. Contract and agent

- [x] 1.1 Add `RunLimits.SuspectedBugs` (0–3/3) and optional `MaxSuspectedBugs` on `TestGenRequest`, checked in `Problem()`; give `TestGuardrails.Check` a `maxBugs` parameter; verify with xUnit (4 rejected, absent → 3, limit 1 flags the second bug)
- [x] 1.2 Use the task's limit in the agent's instructions, `report_suspected_bug` and the handler's guardrail check; verify with agent tests (limit 1 refuses a second report, limit 0 stated in the attempt input)

## 2. Api

- [x] 2.1 Add `DeadlineMinutes` and `MaxSuspectedBugs` to `RunLimitsInput`, `RunLimitsSummary`, the run row and `RunLimitsDto` (as bounds); validate at start (`400 limits`), store, pass the bug limit to the task; verify with api tests (defaults, out of bounds)
- [x] 2.2 Enforce the run's own deadline in `RunFollower` and verify with the run's bug limit in `RunVerifier`; verify with api tests (short deadline fails the run with `deadline`)

## 3. Web

- [x] 3.1 Update `types.ts` and `limits.ts` for the two limits; verify `limits.test.ts`
- [x] 3.2 Render them as fields in `ModelPicker` (read-only list keeps the target), send them; verify with the picker Vitest test (defaults 120 and 3, 5-minute deadline invalid, values sent)

## 4. Verification

- [x] 4.1 Run `make test` and `make lint`; both pass
- [x] 4.2 Rebuild (`make`) and open the model step at http://localhost:7171: deadline 120 and suspected bugs 3 are editable fields

## 5. Documentation

- [x] 5.1 Update `docs/http-api.md` for the models response and the runs body and summary `limits`
- [x] 5.2 Add a DECISIONS.md entry for the two editable limits
- [x] 5.3 Run `make docs` and `make docs-check`; both succeed
