# Fee schedule note: NW-REBAL-2024-105

Internal reference: NW-CANARY-7731-FS105. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-REBAL-2024-105, a household rebalancing fee schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-REBAL-2024-105 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,250,000 | 100 |
| $1,250,000 – $2,750,000 | 90 |
| $2,750,000 – $4,250,000 | 75 |
| Above $4,250,000 | 65 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-REBAL-2024-105 charges a household rebalancing fee of 5 bps of the rebalanced notional, with an annual cap of $7,500. A rebalance event is recorded when the system detects a drift of more than 7% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-REBAL-2024-105, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS105 when contacting the Northwind Capital billing desk.
