# Fee schedule note: NW-HH-2025-010

Internal reference: NW-CANARY-7731-FS010. Owner: D. Albright, Northwind Capital billing operations.

## Overview

Fee schedule NW-HH-2025-010 is one of the household aggregated tiered schedules in the Northwind Capital fee schedule catalog. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $1,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-010 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,000,000 | 105 |
| $1,000,000 – $3,750,000 | 95 |
| $3,750,000 – $4,500,000 | 85 |
| $4,500,000 – $5,250,000 | 65 |
| $5,250,000 – $8,000,000 | 50 |
| Above $8,000,000 | 40 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-HH-2025-010 is a flat $500 per rebalance event, capped at $5,000 per household per year. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-010, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS010 when contacting the Northwind Capital billing desk.
