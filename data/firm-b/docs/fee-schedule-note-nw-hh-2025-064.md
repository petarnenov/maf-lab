# Fee schedule note: NW-HH-2025-064

Internal reference: NW-CANARY-7731-FS064. Owner: T. Varga, Northwind Capital billing operations.

## Overview

Fee schedule NW-HH-2025-064 is one of the household aggregated tiered schedules in the Northwind Capital fee schedule catalog. It is the default for households onboarded through the wealth planning channel. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $1,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-064 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,500,000 | 100 |
| $1,500,000 – $3,750,000 | 80 |
| $3,750,000 – $6,500,000 | 60 |
| $6,500,000 – $8,500,000 | 50 |
| $8,500,000 – $9,000,000 | 40 |
| Above $9,000,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-HH-2025-064 charges a household rebalancing fee of 8 bps of the rebalanced notional, with an annual cap of $5,000. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-064, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS064 when contacting the Northwind Capital billing desk.
