# Held-Away Assets

## Definition

Held-away assets are accounts that Contoso Advisors advises on or reports on but does not hold at its primary custodian. Common examples are employer 401(k) plans, 529 plans held directly with a state program, and annuities. Contoso Advisors includes these assets in planning reports so clients see their full financial picture. Under the flat-fee model, held-away assets do not change the fee, because CONTOSO-FLAT-100 does not depend on asset value.

## Billing Treatment

### Non-Billable Flag

Held-away accounts are loaded onto the TAMP platform through an aggregation feed. Each one must be flagged as non-billable on the household. If the flag is missing, the platform may try to allocate part of the flat fee to an account that cannot be debited, and the custodian fee file will be rejected.

### Legacy Households

For legacy asset-based households, held-away accounts are excluded from the AUM used to calculate the fee unless the agreement explicitly includes them. Including them by mistake would inflate the fee. The operations associate checks the non-billable flags on legacy households every quarter.

## Data Quality

Aggregation feeds are sometimes delayed. A delayed held-away feed does not block the billing run, because the accounts are non-billable, but the planning report may show stale values. Advisors are told to check the as-of date on held-away balances before client meetings.
