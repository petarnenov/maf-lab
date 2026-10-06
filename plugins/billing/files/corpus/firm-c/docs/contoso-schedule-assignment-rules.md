# Fee Schedule Assignment Rules

## Default Assignment

Every billable account at Contoso Advisors must have a fee schedule before a billing run can complete. The default is CONTOSO-FLAT-100, and because the firm bills at the household level, all accounts in a household share the same schedule. The operations associate assigns the schedule when the account is linked to the household. An account linked without a schedule causes the quarterly run to fail with FS-REQUIRED, which is why the schedule assignment review is part of the preparation checklist.

## Exceptions

### Legacy Schedule

Households that remained on asset-based pricing are assigned CONTOSO-AUM-LEGACY. New accounts added to a legacy household receive the same legacy schedule, and the associate confirms this with the operations lead because the account increases the household fee.

### Non-Billable Accounts

Held-away accounts and accounts reported for planning only are flagged non-billable instead of receiving a schedule. Non-billable accounts do not cause FS-REQUIRED failures.

## Verification

Before each quarterly run the associate runs the unassigned-accounts report on the platform. The report must be empty before the run starts. The operations lead spot-checks five households each quarter to confirm that the schedule on the platform matches the client agreement. Mismatches are corrected before the run and noted in the billing binder.
