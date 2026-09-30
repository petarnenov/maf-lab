# Tasks

No TypeSafe Jev call is added or changed, so the Jev review checklist does not apply.

## 1. The cap in one place

- [x] 1.1 `TestGenRequest.AttemptLimit = 10`. `TestAgentOptions.MaxAttempts` (api) defaults to `TestGenRequest.AttemptLimit`. Remove `"MaxAttempts": 5` from `src/Maf.Lab.Api/appsettings.json`. Verify: `grep` finds no other literal attempt cap in `src/` or `web/src/`, and the api models test expects `maxAttempts` equal to `TestGenRequest.AttemptLimit`
- [x] 1.2 `RunDeadline` defaults to 2 h, with its comment updated. Verify: build
- [x] 1.3 Text: the agent card and the handler comment say "the attempt cap" (the card interpolates the limit), and `RaiseThresholdDialog` drops "in up to five attempts". Verify: `TestAgentTests` card test and `ThresholdControl.test.tsx` still pass, adjusted where they quote the text
- [x] 1.4 Tests that assume 5: the invalid-attempts theory asks for `AttemptLimit + 1`, and a new agent test accepts a task of 10 attempts. Verify: `make test`

## 2. Verification

- [x] 2.1 `make lint` and `make test` pass — Done: lint clean; 1071 of 1072 pass. The one failure is the known timing test `GuardrailTests.A_hanging_Jev_costs_no_more_than_the_timeouts`, which fails only under the parallel `make test` load
- [ ] 2.2 After `make`, the model picker on http://localhost:7171 says "up to 10 attempts". Verify: read it in the page
- [x] 2.3 `openspec validate raise-attempt-cap --strict` passes
