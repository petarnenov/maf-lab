# Legacy Asset-Based Clients

## Background

Before Contoso Advisors adopted flat-fee pricing, clients were billed as a percentage of assets under management. A small group of long-standing households chose to remain on asset-based pricing when the firm moved to CONTOSO-FLAT-100. These households are billed under the schedule CONTOSO-AUM-LEGACY, which charges a single percentage rate on household AUM with no breakpoints. The firm does not offer this schedule to new clients, and the operations team reviews the list of legacy households each year.

## Billing Differences

### Valuation Sensitivity

Unlike flat-fee households, legacy households have fees that depend directly on the quarter-end valuation. A stale or missing custodian price therefore changes the invoice amount, not just the allocation across accounts. The operations associate checks legacy household valuations individually before each run and resolves any AUM-STALE warnings before starting.

### Household Aggregation

Legacy households aggregate all linked accounts to compute AUM, and the resulting fee is allocated back to accounts by value. Accounts excluded from billing, such as held-away 401(k) plans reported for planning purposes only, must be flagged as non-billable so they do not inflate the fee.

## Migration

Legacy clients may move to CONTOSO-FLAT-100 at any time by signing an amendment. The move takes effect at the start of the next quarter to avoid mid-quarter proration between schedules. The operations lead updates the schedule assignment and notes the amendment date on the household.
