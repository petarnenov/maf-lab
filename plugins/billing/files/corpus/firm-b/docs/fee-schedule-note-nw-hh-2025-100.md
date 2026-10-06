# Fee schedule note: NW-HH-2025-100

Internal reference: NW-CANARY-7731-FS100. Owner: R. Hollis, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-HH-2025-100 as a household aggregated tiered schedule for advisory households. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-100 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,750,000 | 95 |
| $1,750,000 – $2,500,000 | 75 |
| $2,500,000 – $5,250,000 | 60 |
| Above $5,250,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $750, and the household rebalancing fee is capped at $7,500 per calendar year. A rebalance event is recorded when the system detects a client-initiated reallocation between household accounts. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-100, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS100 when contacting the Northwind Capital billing desk.
