# Fee schedule note: NW-HH-2025-106

Internal reference: NW-CANARY-7731-FS106. Owner: K. Petrakis, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-HH-2025-106 as a household aggregated tiered schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-106 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,000,000 | 100 |
| $1,000,000 – $3,250,000 | 90 |
| $3,250,000 – $4,750,000 | 70 |
| Above $4,750,000 | 55 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-HH-2025-106 is a flat $750 per rebalance event, capped at $5,000 per household per year. A rebalance event is recorded when the system detects a drift of more than 7% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-106, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS106 when contacting the Northwind Capital billing desk.
