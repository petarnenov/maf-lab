# Fee schedule note: NW-TIER-2024-000

Internal reference: NW-CANARY-7731-FS000. Owner: T. Varga, Northwind Capital billing operations.

## Overview

Fee schedule NW-TIER-2024-000 is one of the tiered schedules in the Northwind Capital fee schedule catalog. It is the default for households onboarded through the wealth planning channel. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-TIER-2024-000 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,250,000 | 90 |
| $2,250,000 – $3,500,000 | 80 |
| $3,500,000 – $5,250,000 | 60 |
| $5,250,000 – $5,750,000 | 45 |
| Above $5,750,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-TIER-2024-000 charges a household rebalancing fee of 8 bps of the rebalanced notional, with an annual cap of $5,000. A rebalance event is recorded when the system detects a drift of more than 5% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-TIER-2024-000, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS000 when contacting the Northwind Capital billing desk.
