# Fee schedule note: NW-BRK-2025-085

Internal reference: NW-CANARY-7731-FS085. Owner: T. Varga, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-BRK-2025-085 as a breakpoint schedule for advisory households. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-BRK-2025-085 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,500,000 | 90 |
| $2,500,000 – $5,250,000 | 80 |
| $5,250,000 – $6,500,000 | 70 |
| $6,500,000 – $7,750,000 | 50 |
| $7,750,000 – $10,250,000 | 40 |
| Above $10,250,000 | 30 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-BRK-2025-085 is a flat $250 per rebalance event, capped at $5,000 per household per year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-BRK-2025-085, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS085 when contacting the Northwind Capital billing desk.
