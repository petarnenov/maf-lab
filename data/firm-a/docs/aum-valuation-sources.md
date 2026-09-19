# AUM Valuation Sources at Acme Wealth Partners

## Price and Position Feeds

Acme Wealth Partners values client assets using end-of-day positions from each custodian and prices from the platform's consolidated pricing feed. Positions for the two primary custodians arrive automatically every business day; the legacy custodian's trust accounts are loaded weekly from a manual statement upload.

## Staleness Thresholds

### Standard Accounts

A valuation is considered stale if the latest position date is more than three business days older than the billing period end. Stale valuations trigger an AUM-STALE failure and block the billing run.

### Trust Accounts at the Legacy Custodian

Because statements arrive weekly, the firm allows up to seven business days of staleness for the legacy custodian. OPS must still upload the quarter-end statement before starting the run.

## Illiquid and Alternative Assets

Private funds and other alternatives are valued using the most recent capital account statement. If no statement exists for the quarter, the prior value is carried forward and flagged. Carried-forward values over $1,000,000 must be reviewed by a FIRM_ADMIN before the run starts.
