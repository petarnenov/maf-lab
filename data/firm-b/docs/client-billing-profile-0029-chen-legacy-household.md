# Client billing profile: Chen Legacy Household

Internal reference: NW-CANARY-7731-HH0029. Relationship advisor: E. Lindgren. Firm: Northwind Capital.

## Household summary

This profile covers the Chen Legacy Household, a Northwind Capital household on the flat fee schedule NW-FLAT-2026-074. All 7 accounts are aggregated into one household for breakpoint purposes, giving household AUM of roughly $17,390,000. Invoices are generated monthly in arrears and fees are deducted directly from the primary custodial account at State Street. Any invoice adjustment for this household must be approved by a FIRM_ADMIN and recorded against internal reference NW-CANARY-7731-HH0029.

## Linked accounts

- Roth IRA at State Street, account suffix …8601
- SEP IRA at State Street, account suffix …4961
- SEP IRA at State Street, account suffix …9073
- Custodial UTMA at State Street, account suffix …4432
- Traditional IRA at State Street, account suffix …2146
- SEP IRA at State Street, account suffix …1486
- Joint brokerage at State Street, account suffix …1424

New accounts opened mid-period are prorated from their funding date and join the household aggregate from the next billing period.

## Fee schedule and household rebalancing fee

Household rebalancing is triggered by a drift of more than 7% from the household target allocation. The fee schedule NW-FLAT-2026-074 bills each household rebalancing event separately; with 7 events last year the capped household rebalancing fee was about $3,500. The asset-based fee and the household rebalancing fee are shown as separate lines on the household invoice.

## Billing notes

The last annual billing review corrected the proration start date for a newly funded account. The advisor, E. Lindgren, confirms the fee schedule and household rebalancing fee terms with the client at each review. If a billing run fails with FS-REQUIRED or CUSTODIAN-MISMATCH for this household, OPS should check the fee schedule assignment and the State Street fee deduction before re-running.
