# Fee schedule note: NW-TIER-2024-108

Internal reference: NW-CANARY-7731-FS108. Owner: K. Petrakis, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-TIER-2024-108 as a tiered schedule for advisory households. It is the default for households onboarded through the wealth planning channel. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $1,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-TIER-2024-108 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,500,000 | 110 |
| $2,500,000 – $3,750,000 | 100 |
| $3,750,000 – $6,250,000 | 80 |
| Above $6,250,000 | 70 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-TIER-2024-108 is a flat $350 per rebalance event, capped at $7,500 per household per year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-TIER-2024-108, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS108 when contacting the Northwind Capital billing desk.
