# Tasks

## 1. Compliance threshold by size

- [x] 1.1 In `ComplianceAgentTests`, add failing tests: -4,116 is refused with a reason naming the threshold, and -1,000 at a 1,000 threshold is not refused. Verify with `dotnet test --filter ComplianceAgentTests` that the new test for the large credit fails.
- [x] 1.2 In `ReviewAgentHandler`, compare `Math.Abs(adjustment.Amount)` against `RefuseAboveAmount`, then verify `dotnet test --filter ComplianceAgentTests` passes.

## 2. Ledger refuses a reduction below zero

- [x] 2.1 In `FeeAdjustmentLedgerTests`, add failing tests: a reduction that would go below zero throws and leaves `AppliedTotal` unchanged; a reduction to exactly zero applies; two -500 reductions on a seed of 812 apply the first and refuse the second (total -500); a +1,000 increase on a fee already below zero applies; the same adjustment confirmed again after the fee has dropped to zero reports already applied. Verify the new tests fail.
- [x] 2.2 Add `FeeWouldGoBelowZeroException` (internal, in `Maf.Lab.Retrieval.Billing`, carrying the previous and resulting fee). In `FeeAdjustmentLedger.Apply`, after the already-applied read and before the insert, roll back and throw it when `amount < 0 && previousFee + amount < 0`. Verify with `dotnet test --filter FeeAdjustmentLedgerTests` that the tests pass, including the existing race and idempotency tests.

## 3. Tool refuses at proposal and reports a refused apply

- [x] 3.1 In `FeeAdjustmentToolTests`, add failing cases: a proposal of -1,300 on A-1042 (seed 1,200) is an error containing "below zero", with no input requested and the fee unchanged (extend the cannot-stand theory); a confirmed proposal whose apply is refused by the ledger, because another adjustment landed first, is an error containing "below zero", writes nothing, and records no idempotency key. Verify the new tests fail.
- [x] 3.2 In `FeeAdjustmentTools.Proposed`, refuse `amount < 0 && account.Fee + amount < 0` with the shared message. In `Confirmed`, catch `FeeWouldGoBelowZeroException` around `ledger.Apply` and return the same message as a tool error. Verify with `dotnet test --filter FeeAdjustmentToolTests` that the tests pass.

## 4. Record and verify

- [x] 4.1 Add a `DECISIONS.md` section for this change: the rule, why it is checked in the ledger transaction, why only reductions are refused, and that carry-forward is deferred. Verify the section is present and cites the proposal's date.
- [x] 4.2 Run `make test` and `make lint` and verify both pass.
- [x] 4.3 Rebuild the stack with `make`. Through http://localhost:7171, propose a credit on A-1042 that would take it below zero (from its current -6,048) and verify the chat shows the refusal and the `PendingAdjustments` table gains no `applied` row for it. Then propose +1,000 and verify it is applied.
- [x] 4.4 Run `openspec validate guard-fee-adjustment-sign --strict` and verify it passes.
