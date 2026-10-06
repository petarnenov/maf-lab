# Fee schedule note: NW-FLAT-2026-044

Internal reference: NW-CANARY-7731-FS044. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

Fee schedule NW-FLAT-2026-044 is one of the flat schedules in the Northwind Capital fee schedule catalog. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-044 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,250,000 | 90 |
| $1,250,000 – $3,500,000 | 75 |
| $3,500,000 – $5,000,000 | 65 |
| $5,000,000 – $7,000,000 | 45 |
| $7,000,000 – $9,000,000 | 35 |
| Above $9,000,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $350, and the household rebalancing fee is capped at $5,000 per calendar year. A rebalance event is recorded when the system detects a tax-loss harvesting rebalance across the household. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-044, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS044 when contacting the Northwind Capital billing desk.
