# Fee schedule note: NW-BRK-2025-061

Internal reference: NW-CANARY-7731-FS061. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

Fee schedule NW-BRK-2025-061 is one of the breakpoint schedules in the Northwind Capital fee schedule catalog. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-BRK-2025-061 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,250,000 | 110 |
| $1,250,000 – $2,250,000 | 95 |
| $2,250,000 – $5,000,000 | 80 |
| $5,000,000 – $6,500,000 | 65 |
| $6,500,000 – $8,250,000 | 55 |
| Above $8,250,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-BRK-2025-061 charges a household rebalancing fee of 8 bps of the rebalanced notional, with an annual cap of $2,500. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-BRK-2025-061, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS061 when contacting the Northwind Capital billing desk.
