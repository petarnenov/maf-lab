# Billing Run Failure Codes

## How Failure Codes Work

When a billing run fails, the failure reason always begins with a stable code followed by a colon and a description, for example "FS-REQUIRED: fee schedule missing for 3 accounts". The code is intended for filtering, dashboards, and support tooling, while the description gives the specifics of the failed run. Each code maps to a documented procedure, and resolving the underlying problem is always required before a re-run can succeed. Codes never change meaning between platform releases, so saved searches and alerts remain valid over time.

## Codes

### FS-REQUIRED, AUM-STALE, CUSTODIAN-MISMATCH and PRORATION-GAP

**FS-REQUIRED.** FS-REQUIRED means that one or more billable accounts could not resolve a fee schedule. This usually happens when new accounts were opened during the period and were not assigned a schedule, when a household was dissolved and its schedule assignment was lost, or when a schedule was retired without a replacement. Follow the procedure "Procedure: Missing fee schedule" to identify the accounts, assign a schedule, validate, and re-run.

**AUM-STALE.** AUM-STALE means that the valuation for one or more accounts is older than the allowed staleness window, which is three business days before the valuation date by default. Stale valuations usually come from a delayed custodian feed or from accounts whose positions failed to price. Follow "Correcting a stale AUM valuation".

**CUSTODIAN-MISMATCH.** CUSTODIAN-MISMATCH means the account's custodian record does not match the platform, for example a closed account, a changed account number, or cash too low to cover a direct-debit fee. The run fails so that no fee file is sent with invalid instructions. Reconcile the account with the custodian before re-running.

**PRORATION-GAP.** PRORATION-GAP means an account has an inconsistent timeline in the period, such as a funding date after its closing date, overlapping schedule assignments, or a gap between two schedule versions. The engine refuses to guess the billable days. Correct the dates on the account or schedule assignment and then re-run.

## Non-Blocking Warnings

### Warnings Versus Failures

Some conditions produce warnings rather than failures, such as a fee below the schedule minimum or an account with zero AUM. Warnings appear in run diagnostics and on the review screen but do not stop the run from completing. Reviewers should still check warnings before approving invoices, because a warning such as zero AUM often points to a data issue at the custodian. Firms can promote selected warnings to failures in their billing settings if they prefer stricter control, for example to stop a run whenever an account would be charged its schedule minimum.
