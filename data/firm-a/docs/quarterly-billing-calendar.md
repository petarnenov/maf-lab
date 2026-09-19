# Quarterly Billing Calendar at Acme Wealth Partners

## Standard Timeline

Acme Wealth Partners bills in arrears, so each quarterly billing run covers the quarter that has just ended. The timeline below is measured in business days after quarter end and applies to all households on tiered schedules, including ACME-TIER-2026.

### Business Days 1 to 3

Custodian positions and prices are loaded and the AUM valuation is refreshed. OPS reviews the stale-valuation report and resolves any AUM-STALE warnings before the billing run is started.

### Business Day 4

The billing run is started. Failed runs must be triaged the same day, and the billing desk is notified of every failure code.

### Business Days 5 to 7

FIRM_ADMIN users approve invoices over $25,000. Fee deduction files are submitted to custodians on day five for approved invoices, and supplemental files follow as approvals complete.

## Period Close

The billing period is closed no later than the fifteenth business day after quarter end, once custodian reconciliation is complete and every CUSTODIAN-MISMATCH exception has been resolved. After close, changes require a billing credit or adjustment in the next period.
