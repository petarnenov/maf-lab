# Fee schedule note: NW-BRK-2025-109

Internal reference: NW-CANARY-7731-FS109. Owner: K. Petrakis, Northwind Capital billing operations.

## Overview

Fee schedule NW-BRK-2025-109 is one of the breakpoint schedules in the Northwind Capital fee schedule catalog. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $1,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-BRK-2025-109 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,000,000 | 100 |
| $2,000,000 – $2,750,000 | 80 |
| $2,750,000 – $4,250,000 | 60 |
| Above $4,250,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-BRK-2025-109 is a flat $500 per rebalance event, capped at $5,000 per household per year. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-BRK-2025-109, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS109 when contacting the Northwind Capital billing desk.
