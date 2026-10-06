# Fee schedule note: NW-INST-2026-047

Internal reference: NW-CANARY-7731-FS047. Owner: D. Albright, Northwind Capital billing operations.

## Overview

Fee schedule NW-INST-2026-047 is one of the institutional breakpoint schedules in the Northwind Capital fee schedule catalog. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-047 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,000,000 | 100 |
| $1,000,000 – $2,500,000 | 85 |
| $2,500,000 – $3,250,000 | 65 |
| Above $3,250,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $500, and the household rebalancing fee is capped at $2,500 per calendar year. A rebalance event is recorded when the system detects a client-initiated reallocation between household accounts. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-047, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS047 when contacting the Northwind Capital billing desk.
