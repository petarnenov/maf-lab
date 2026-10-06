# Fee schedule note: NW-HH-2025-118

Internal reference: NW-CANARY-7731-FS118. Owner: E. Lindgren, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-HH-2025-118, a household aggregated tiered schedule used by Northwind Capital for household billing. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-118 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 95 |
| $2,750,000 – $4,750,000 | 85 |
| $4,750,000 – $7,500,000 | 65 |
| $7,500,000 – $8,750,000 | 45 |
| Above $8,750,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-HH-2025-118 charges a household rebalancing fee of 2 bps of the rebalanced notional, with an annual cap of $2,500. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-118, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS118 when contacting the Northwind Capital billing desk.
