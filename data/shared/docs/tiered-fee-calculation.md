# Tiered Fee Calculation

## How Tiers Are Applied

Tiered calculation walks through the tiers of a schedule in ascending order. For each tier, the engine takes the portion of billable AUM that falls between the tier's lower bound and upper bound and multiplies it by the tier's annual rate. The results are summed to produce the annual fee, which is then scaled to the billing period using the day-count convention configured for the firm. The last tier of a schedule always has an open upper bound so that any amount of AUM is covered.

## Worked Example

### Inputs and Result

**Inputs.** Consider a household with $6,500,000 of billable AUM on a tiered schedule with three tiers: 1.00% on the first $1,000,000, 0.75% on the next $4,000,000, and 0.50% above $5,000,000. The billing period is a calendar quarter billed in arrears, and the household held the assets for the full quarter.

**Result.** The first tier produces $10,000, the second tier produces $30,000, and the third tier produces $7,500 on the remaining $1,500,000. The annual fee is $47,500, an effective rate of about 0.73%. Using an actual/365 convention for a 92-day quarter, the period fee is $47,500 × 92 / 365, or $11,972.60 after rounding to the cent.

## Rounding and Precision

### Rules

All intermediate amounts are calculated with decimal precision and are never rounded per tier. Rounding happens once, at the account fee level, using banker's rounding to two decimal places. When a household fee is allocated back to its member accounts, the allocation is proportional to each account's AUM, and any remaining cent from rounding is assigned to the account with the largest balance. This guarantees that the sum of account fees always equals the household fee shown on the invoice.
