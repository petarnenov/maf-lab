# AUM Valuation

## Source of Valuations

Billable AUM is derived from positions and prices received from custodians each business day. The valuation service combines custodian positions with platform pricing, applies firm exclusions, and stores one valuation per account per date. The billing engine never prices securities itself; it reads the stored valuation for the valuation date of the run. This guarantees that the same figure appears on client statements, performance reports, and invoices. If a custodian later corrects a price, the stored valuation is versioned so that the value used by an earlier run remains available for audit.

## Valuation Date and Staleness

### Choosing the Valuation Date and Staleness Window

**Choosing the Valuation Date.** For arrears billing, the valuation date is normally the last business day of the period, or the period average if the firm uses average daily balance. For advance billing, it is the last business day before the period starts. The valuation date is fixed when the run is created and is shown on every invoice.

**Staleness Window.** A valuation is stale when the latest available record for an account is older than the staleness window, three business days by default, relative to the valuation date. Stale valuations cause the run to fail with AUM-STALE rather than silently billing on old values. The failure lists each affected account with the date of its last good valuation.

## Exclusions and Overrides

### Excluded Assets and Manual Overrides

**Excluded Assets.** Firms can exclude asset classes, individual securities, or cash from billable AUM. Exclusions are effective-dated and appear on the invoice as a reduced billable value, with the excluded amount disclosed separately.

**Manual Overrides.** When a custodian valuation is wrong, OPS can enter a manual override for a specific account and date with supporting evidence. Overrides require approval, are flagged on the invoice, and are replaced automatically if the custodian later sends a corrected file. Both exclusions and overrides are listed in the run diagnostics so that reviewers can see why billable AUM differs from the custodian market value.
