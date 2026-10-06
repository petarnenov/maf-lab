# Fee schedule note: NW-TIER-2024-114

Internal reference: NW-CANARY-7731-FS114. Owner: T. Varga, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-TIER-2024-114, a tiered schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-TIER-2024-114 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,250,000 | 90 |
| $2,250,000 – $4,750,000 | 70 |
| $4,750,000 – $7,500,000 | 55 |
| $7,500,000 – $9,250,000 | 40 |
| $9,250,000 – $9,750,000 | 25 |
| Above $9,750,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-TIER-2024-114 is a flat $750 per rebalance event, capped at $10,000 per household per year. A rebalance event is recorded when the system detects a drift of more than 3% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-TIER-2024-114, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS114 when contacting the Northwind Capital billing desk.
