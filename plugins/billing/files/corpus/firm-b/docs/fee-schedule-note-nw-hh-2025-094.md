# Fee schedule note: NW-HH-2025-094

Internal reference: NW-CANARY-7731-FS094. Owner: R. Hollis, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-HH-2025-094 as a household aggregated tiered schedule for advisory households. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-094 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,500,000 | 95 |
| $1,500,000 – $4,250,000 | 80 |
| $4,250,000 – $5,500,000 | 65 |
| $5,500,000 – $7,250,000 | 55 |
| Above $7,250,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $500, and the household rebalancing fee is capped at $7,500 per calendar year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-094, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS094 when contacting the Northwind Capital billing desk.
