# CONTOSO-FLAT-100 Fee Schedule

## Summary

CONTOSO-FLAT-100 is the default fee schedule for Contoso Advisors. It charges a flat annual advisory fee per household, independent of assets under management. The schedule was introduced so that clients with modest portfolios pay a transparent fee that covers financial planning, investment management and annual reviews. The schedule code is referenced in every client agreement signed after the schedule went live, and the operations team uses the code when assigning schedules to new accounts on the TAMP platform.

## Fee Terms

### Annual Amount

The standard annual fee under CONTOSO-FLAT-100 is set per household tier. Core households pay a fixed amount per year, and households with complex planning needs pay a higher fixed amount. The amount is recorded on the household, not on individual accounts, so that adding a new account to an existing household does not increase the fee. The quarterly installment equals one quarter of the annual amount, rounded to the nearest cent, with any rounding difference applied to the fourth quarter.

### Allocation Across Accounts

Because the platform bills at the account level, the household fee is allocated across accounts in proportion to their market value at the start of the quarter. Accounts with a zero balance receive no allocation. If a household contains a single account, the entire installment is debited from that account. The allocation is recalculated every quarter.

## Governance

Changes to CONTOSO-FLAT-100 require a written amendment approved by the Contoso Advisors managing partner. The operations team keeps the approval memo under reference CONTOSO-CANARY-2290 in the compliance archive. Schedule edits made on the platform must match the approved memo exactly, and a second FIRM_ADMIN reviews the edit before the next billing run is started.
