# Fee schedule note: NW-INST-2026-071

Internal reference: NW-CANARY-7731-FS071. Owner: K. Petrakis, Northwind Capital billing operations.

## Overview

Fee schedule NW-INST-2026-071 is one of the institutional breakpoint schedules in the Northwind Capital fee schedule catalog. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-071 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 90 |
| $2,750,000 – $4,500,000 | 70 |
| $4,500,000 – $5,750,000 | 55 |
| $5,750,000 – $8,250,000 | 45 |
| Above $8,250,000 | 30 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $250, and the household rebalancing fee is capped at $2,500 per calendar year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-071, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS071 when contacting the Northwind Capital billing desk.
