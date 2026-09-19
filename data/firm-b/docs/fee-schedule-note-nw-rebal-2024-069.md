# Fee schedule note: NW-REBAL-2024-069

Internal reference: NW-CANARY-7731-FS069. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-REBAL-2024-069 as a household rebalancing fee schedule for advisory households. It is the default for households onboarded through the wealth planning channel. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-REBAL-2024-069 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,000,000 | 110 |
| $2,000,000 – $4,250,000 | 95 |
| $4,250,000 – $6,750,000 | 75 |
| $6,750,000 – $7,750,000 | 55 |
| $7,750,000 – $8,750,000 | 35 |
| Above $8,750,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-REBAL-2024-069 is a flat $350 per rebalance event, capped at $2,500 per household per year. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-REBAL-2024-069, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS069 when contacting the Northwind Capital billing desk.
