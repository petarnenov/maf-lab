# Fee schedule note: NW-INST-2026-005

Internal reference: NW-CANARY-7731-FS005. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

Fee schedule NW-INST-2026-005 is one of the institutional breakpoint schedules in the Northwind Capital fee schedule catalog. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-005 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,250,000 | 90 |
| $1,250,000 – $2,500,000 | 75 |
| $2,500,000 – $4,500,000 | 60 |
| $4,500,000 – $5,250,000 | 50 |
| Above $5,250,000 | 35 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-INST-2026-005 charges a household rebalancing fee of 5 bps of the rebalanced notional, with an annual cap of $7,500. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-005, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS005 when contacting the Northwind Capital billing desk.
