# Fee schedule note: NW-INST-2026-023

Internal reference: NW-CANARY-7731-FS023. Owner: D. Albright, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-INST-2026-023 as a institutional breakpoint schedule for advisory households. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-023 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $750,000 | 100 |
| $750,000 – $3,250,000 | 80 |
| $3,250,000 – $5,000,000 | 70 |
| Above $5,000,000 | 60 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $350, and the household rebalancing fee is capped at $7,500 per calendar year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-023, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS023 when contacting the Northwind Capital billing desk.
