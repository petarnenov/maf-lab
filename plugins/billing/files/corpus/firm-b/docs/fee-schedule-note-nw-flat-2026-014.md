# Fee schedule note: NW-FLAT-2026-014

Internal reference: NW-CANARY-7731-FS014. Owner: R. Hollis, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-FLAT-2026-014, a flat schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-014 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,750,000 | 95 |
| $1,750,000 – $4,500,000 | 85 |
| $4,500,000 – $6,750,000 | 65 |
| $6,750,000 – $8,750,000 | 50 |
| Above $8,750,000 | 30 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-FLAT-2026-014 charges a household rebalancing fee of 8 bps of the rebalanced notional, with an annual cap of $5,000. A rebalance event is recorded when the system detects a drift of more than 7% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-014, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS014 when contacting the Northwind Capital billing desk.
