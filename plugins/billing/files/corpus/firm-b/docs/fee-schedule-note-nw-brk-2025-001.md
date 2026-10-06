# Fee schedule note: NW-BRK-2025-001

Internal reference: NW-CANARY-7731-FS001. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-BRK-2025-001, a breakpoint schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-BRK-2025-001 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,750,000 | 95 |
| $1,750,000 – $3,000,000 | 80 |
| $3,000,000 – $4,000,000 | 60 |
| $4,000,000 – $6,250,000 | 40 |
| $6,250,000 – $8,500,000 | 30 |
| Above $8,500,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-BRK-2025-001 is a flat $150 per rebalance event, capped at $2,500 per household per year. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-BRK-2025-001, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS001 when contacting the Northwind Capital billing desk.
