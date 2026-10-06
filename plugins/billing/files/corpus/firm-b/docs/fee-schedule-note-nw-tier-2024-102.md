# Fee schedule note: NW-TIER-2024-102

Internal reference: NW-CANARY-7731-FS102. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-TIER-2024-102 as a tiered schedule for advisory households. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-TIER-2024-102 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,250,000 | 105 |
| $1,250,000 – $3,500,000 | 85 |
| $3,500,000 – $4,500,000 | 70 |
| $4,500,000 – $6,750,000 | 60 |
| $6,750,000 – $9,500,000 | 45 |
| Above $9,500,000 | 30 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-TIER-2024-102 is a flat $250 per rebalance event, capped at $2,500 per household per year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-TIER-2024-102, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS102 when contacting the Northwind Capital billing desk.
