# Fee schedule note: NW-BRK-2025-013

Internal reference: NW-CANARY-7731-FS013. Owner: K. Petrakis, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-BRK-2025-013 as a breakpoint schedule for advisory households. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-BRK-2025-013 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,250,000 | 105 |
| $1,250,000 – $4,000,000 | 90 |
| $4,000,000 – $4,750,000 | 80 |
| $4,750,000 – $5,250,000 | 70 |
| $5,250,000 – $7,250,000 | 55 |
| Above $7,250,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $500, and the household rebalancing fee is capped at $10,000 per calendar year. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-BRK-2025-013, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS013 when contacting the Northwind Capital billing desk.
