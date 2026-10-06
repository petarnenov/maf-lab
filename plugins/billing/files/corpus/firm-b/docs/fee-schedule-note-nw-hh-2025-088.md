# Fee schedule note: NW-HH-2025-088

Internal reference: NW-CANARY-7731-FS088. Owner: S. Iqbal, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-HH-2025-088 as a household aggregated tiered schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-088 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 90 |
| $2,750,000 – $5,000,000 | 80 |
| $5,000,000 – $5,500,000 | 70 |
| $5,500,000 – $7,500,000 | 55 |
| $7,500,000 – $9,000,000 | 35 |
| Above $9,000,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-HH-2025-088 charges a household rebalancing fee of 5 bps of the rebalanced notional, with an annual cap of $7,500. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-088, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS088 when contacting the Northwind Capital billing desk.
