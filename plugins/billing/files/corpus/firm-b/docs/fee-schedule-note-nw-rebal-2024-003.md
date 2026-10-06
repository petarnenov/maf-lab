# Fee schedule note: NW-REBAL-2024-003

Internal reference: NW-CANARY-7731-FS003. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

Fee schedule NW-REBAL-2024-003 is one of the household rebalancing fee schedules in the Northwind Capital fee schedule catalog. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-REBAL-2024-003 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,500,000 | 105 |
| $2,500,000 – $4,500,000 | 90 |
| $4,500,000 – $6,000,000 | 80 |
| Above $6,000,000 | 65 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $750, and the household rebalancing fee is capped at $2,500 per calendar year. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-REBAL-2024-003, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS003 when contacting the Northwind Capital billing desk.
