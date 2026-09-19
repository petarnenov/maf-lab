# Failed Billing Runs at Contoso Advisors

## Why Runs Fail

A billing run at Contoso Advisors fails when the platform cannot calculate a fee for at least one account. Because the firm bills all households together in one quarterly run, a single problem account stops the whole run. The most common failure at Contoso Advisors is FS-REQUIRED, which means an account has no fee schedule assigned, usually because a new account was opened at the custodian and linked to the household after the schedule assignment review. The second most common is AUM-STALE, caused by a late custodian price file.

## First Response

### Read the Failure Reason

The run detail page shows the failure code and the number of affected accounts. The operations associate records the code and the account list in the billing log before making any change. This record is used during the post-run review and helps identify recurring onboarding gaps.

### Fix and Re-run

For FS-REQUIRED, the associate assigns CONTOSO-FLAT-100 or the schedule named in the client agreement to each affected account, then re-runs. For AUM-STALE, the associate waits for the corrected custodian file or requests a manual price, then re-runs. A failed run is never edited; the re-run creates fresh results.

## Escalation

If a run fails twice for the same reason, or fails with an unfamiliar code, the associate escalates to the operations lead. The lead decides whether to contact platform support. Invoices are not released until a run completes successfully and passes invoice review.
