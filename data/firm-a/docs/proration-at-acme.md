# Proration Rules at Acme Wealth Partners

## Accounts Opened Mid-Quarter

Accounts funded during a quarter are billed from the funding date to the period end. The fee is calculated using the household's tiered rate, then multiplied by the number of days under management divided by the number of days in the quarter. The first invoice for a new household therefore reflects only the days the assets were managed.

## Accounts Closed Mid-Quarter

Closed accounts are billed through the date of the final distribution. Because Acme bills in arrears, the closing fee is calculated at the next quarterly run rather than deducted at closing, unless the client requests a final invoice.

## Large Contributions and Withdrawals

### Threshold

Contributions or withdrawals greater than $250,000 within a quarter trigger cash-flow proration. The account's average AUM is recalculated using daily balances rather than the quarter-end value.

### Gaps

If the platform finds days in the period without a valuation for an account, the run fails with PRORATION-GAP. OPS fills the missing valuation dates before re-running. Proration never overrides the ACME-TIER-2026 minimum fee, which is itself prorated for partial periods.
