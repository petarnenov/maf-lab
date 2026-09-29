# Proposal

## Why

On 2026-09-29, two credits applied to A-1042 took its fee from 812.00 to -3,304.00 and then to -6,048.00 USD. Two defects allowed this, and neither was noticed at the time:

- The compliance reviewer refuses only adjustments *greater than* its threshold. The comparison is signed, so a credit of any size passes, even though the API sends a credit for review using its absolute size.
- Nothing refuses an adjustment that would take an account's fee below zero: not the proposal, and not the write. The firm's own documentation says an invoice total is never negative (`data/shared/docs/invoice-generation.md`).

## What Changes

- The compliance reviewer judges an adjustment by its size, not its direction: a credit larger than the refusal threshold is refused, the same as an increase that size.
- `propose_fee_adjustment` refuses a proposal whose resulting fee would be below zero. It returns an error saying so, and nothing is asked or written.
- Applying a confirmed adjustment checks the same rule again against the fee at that moment, inside the write. Proposals that each passed alone but together would take the fee below zero apply only while the fee stays at or above zero. The one that would cross zero is refused, and nothing of it is written.
- An adjustment that raises the fee is never refused under this rule. An account already below zero, such as A-1042, can still be corrected upward.
- Out of scope: carrying the unused part of a credit forward to the next period, as the documentation describes. This change only refuses; carry-forward is a separate change.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `fee-adjustment`: a new requirement that a reduction never leaves an account's fee below zero, enforced both when proposing and when applying.
- `compliance-review`: a new requirement that the refusal threshold applies to an adjustment's size in either direction.

## Impact

- `src/Maf.Lab.ComplianceAgent/ReviewAgentHandler.cs`: the refusal comparison.
- `src/Maf.Lab.Retrieval/Tools/FeeAdjustmentTools.cs`: proposal validation, and the handling of a refusal raised by the ledger.
- `src/Maf.Lab.Retrieval/Billing/FeeAdjustmentLedger.cs`: the check inside the apply transaction.
- Tests: `ComplianceAgentTests`, `FeeAdjustmentToolTests`, `FeeAdjustmentLedgerTests`.
- The API needs no change. A confirmation that comes back as an error already marks the pending adjustment `failed` (`ConfirmationService`).
- `DECISIONS.md`: a note recording the rule and why carry-forward was deferred.
- Existing data is left as it is. A-1042 stays at -6,048.00 in the local ledger until it is corrected upward or the volume is reset.
- No package, configuration or schema changes.
