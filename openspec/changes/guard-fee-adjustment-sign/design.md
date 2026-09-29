# Design

## Context

See proposal.md, Why. The pieces involved:

- `ReviewAgentHandler` (compliance service) decides `refused = adjustment.Amount > RefuseAboveAmount`. The API decides whether to ask for a review with `Math.Abs(amount) > ReviewAboveAmount` (`FeeAdjustmentFlow`), so the two sides already disagree about sign.
- `FeeAdjustmentTools.Proposed` builds the summary from `AccountFees.Current`, which is the seeded fee plus the ledger. It validates the account, a zero amount and an empty reason, and nothing else.
- `FeeAdjustmentLedger.Apply` opens an immediate, serializable SQLite transaction. It returns the existing row if the adjustment was already applied; otherwise it computes `previousFee = seed + Σ applied`, inserts, and commits. Both replicas share the file, so this transaction is the only point where the current fee is known without a race.
- When a confirmation call returns a tool error, `ConfirmationService` already marks the pending adjustment `failed` and the UI shows the message.

## Goals / Non-Goals

**Goals:**
- One rule, "a reduction never leaves the fee below zero", checked in both places, with the ledger check being the authoritative one.
- The compliance threshold is symmetric.

**Non-Goals:**
- Carrying the unused part of a credit forward to the next period.
- Correcting existing negative fees in any environment's ledger.
- Changing the API-side review trigger or its threshold (500), which is already symmetric.

## Decisions

- **Check in the ledger, inside the transaction, as well as at proposal.** The check at proposal gives the advisor an early, clear answer, before a reviewer or a person is asked. On its own it is not enough: a proposal is valid for 30 minutes, and other adjustments can land in that time. The check in the ledger runs where the current fee is read and written under one lock, so it holds across replicas.
  - Alternative: check only at proposal. Rejected, because two -500 proposals on 812 would both pass.
  - Alternative: a SQL `CHECK` constraint. Rejected, because `ResultingFee` is stored as text through `Money.Format`, and a constraint would not give the tool a refusal it could tell apart from other errors.
- **"Already applied" is checked before "below zero".** A repeated confirmation is a replay of a fact, not a new write. `Apply` keeps its existing order: first it reads the existing row, then it checks the rule. That way a retry never turns into a refusal after the fee has moved.
- **The ledger signals the refusal with a dedicated exception** (`FeeWouldGoBelowZeroException`, internal to `Maf.Lab.Retrieval`), and the transaction is rolled back. `FeeAdjustmentTools.Confirmed` catches exactly that type and returns `ToolErrors.Error(...)` with the same wording as at proposal. `FeeAdjustmentApplied` is a Domain DTO shared with the API. Adding a "refused" state to it would widen a contract the model and the API both read, for a case that is an error.
  - Alternative: a nullable return. Rejected, because a null would not say why.
- **An idempotency key is not recorded for a refusal.** A retry under the same key reaches the ledger again and gets the same refusal, unless the fee has since risen, in which case applying it is correct. This matches how other tool errors are treated today.
- **Only reductions are refused**, meaning `amount < 0 && previous + amount < 0`. Refusing on `previous + amount < 0` alone would also block raising a fee that is already negative, and A-1042 would then be stuck at -6,048.
- **The compliance fix is `Math.Abs(adjustment.Amount) > RefuseAboveAmount`.** The reason text is unchanged: it already names the threshold, not a direction.
- **Message wording:** "That would take the fee on {account} below zero ({current} → {resulting}). Nothing was changed." It uses the same formatting as `Sentence`, and the proposal and the ledger paths share it.

## Risks / Trade-offs

- [An advisor whose credit is legitimately larger than the fee has no way through] → This is intended until carry-forward exists. The message says why, so the advisor can propose the credit up to the fee.
- [The local ledger still holds A-1042 at -6,048] → It is left alone, and the "Raising a fee that is already below zero" scenario keeps it correctable. `make down` plus removing the `retrieval-data` volume resets it.
- [The compliance service and the retrieval service are deployed separately] → The two fixes are independent. Either one alone is an improvement, so the order of rollout does not matter.

## Migration Plan

Deploy normally. No schema, configuration or data migration is needed. To roll back, revert the commit.
