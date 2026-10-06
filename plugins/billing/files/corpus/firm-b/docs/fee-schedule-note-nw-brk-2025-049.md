# Fee schedule note: NW-BRK-2025-049

Internal reference: NW-CANARY-7731-FS049. Owner: E. Lindgren, Northwind Capital billing operations.

## Overview

Fee schedule NW-BRK-2025-049 is one of the breakpoint schedules in the Northwind Capital fee schedule catalog. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-BRK-2025-049 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 110 |
| $2,750,000 – $5,000,000 | 100 |
| $5,000,000 – $7,000,000 | 80 |
| $7,000,000 – $9,750,000 | 70 |
| $9,750,000 – $11,000,000 | 50 |
| Above $11,000,000 | 35 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-BRK-2025-049 is a flat $350 per rebalance event, capped at $10,000 per household per year. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-BRK-2025-049, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS049 when contacting the Northwind Capital billing desk.
