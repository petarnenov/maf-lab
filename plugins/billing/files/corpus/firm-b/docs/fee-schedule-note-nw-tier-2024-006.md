# Fee schedule note: NW-TIER-2024-006

Internal reference: NW-CANARY-7731-FS006. Owner: S. Iqbal, Northwind Capital billing operations.

## Overview

Fee schedule NW-TIER-2024-006 is one of the tiered schedules in the Northwind Capital fee schedule catalog. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-TIER-2024-006 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 90 |
| $2,750,000 – $4,750,000 | 80 |
| $4,750,000 – $7,500,000 | 65 |
| $7,500,000 – $9,000,000 | 55 |
| Above $9,000,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-TIER-2024-006 is a flat $750 per rebalance event, capped at $7,500 per household per year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-TIER-2024-006, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS006 when contacting the Northwind Capital billing desk.
