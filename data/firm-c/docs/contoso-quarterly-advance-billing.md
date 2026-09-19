# Quarterly in Advance Billing at Contoso Advisors

## Billing Calendar

Contoso Advisors runs four billing periods per year, aligned with calendar quarters. Each run is started on the first business day of the quarter and invoices the upcoming three months. The billing periods are January to March, April to June, July to September, and October to December. The operations team publishes the calendar each December so advisors know when client statements will show the advisory fee debit. If the first business day falls on a market holiday, the run is started on the next business day.

## Valuation Date

Although the flat fee does not depend on assets, the platform still needs a valuation to allocate each household installment across accounts. Contoso Advisors uses the market value as of the last business day of the prior quarter. The operations team confirms that custodian valuations were received before the run begins, because a missing valuation will cause the run to fail with AUM-STALE. For the few asset-based legacy households, the same valuation date determines the fee amount itself.

## Proration

### New Accounts

Accounts opened during a quarter are billed on the next run for the partial period already elapsed, plus the full upcoming quarter. The partial amount is prorated by days, using the actual number of days in the quarter.

### Terminations

When a relationship ends mid-quarter, the unused portion of the prepaid fee is returned as a credit. The credit is calculated by days and applied on the next run, or refunded by check if no accounts remain open. Reconciliation notes for terminations are filed under CONTOSO-CANARY-2290.
