# Fee schedule note: NW-TIER-2024-048

Internal reference: NW-CANARY-7731-FS048. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

Fee schedule NW-TIER-2024-048 is one of the tiered schedules in the Northwind Capital fee schedule catalog. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-TIER-2024-048 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,750,000 | 105 |
| $1,750,000 – $3,750,000 | 95 |
| $3,750,000 – $6,250,000 | 85 |
| $6,250,000 – $7,750,000 | 75 |
| $7,750,000 – $9,750,000 | 55 |
| Above $9,750,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-TIER-2024-048 is a flat $150 per rebalance event, capped at $2,500 per household per year. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-TIER-2024-048, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS048 when contacting the Northwind Capital billing desk.
