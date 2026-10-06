# Fee schedule note: NW-HH-2025-076

Internal reference: NW-CANARY-7731-FS076. Owner: T. Varga, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-HH-2025-076 as a household aggregated tiered schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-076 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,250,000 | 100 |
| $2,250,000 – $4,500,000 | 85 |
| $4,500,000 – $5,000,000 | 75 |
| $5,000,000 – $6,500,000 | 60 |
| $6,500,000 – $8,000,000 | 45 |
| Above $8,000,000 | 30 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $250, and the household rebalancing fee is capped at $5,000 per calendar year. A rebalance event is recorded when the system detects a drift of more than 7% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-076, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS076 when contacting the Northwind Capital billing desk.
