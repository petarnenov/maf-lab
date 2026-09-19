# Fee schedule note: NW-REBAL-2024-087

Internal reference: NW-CANARY-7731-FS087. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-REBAL-2024-087, a household rebalancing fee schedule used by Northwind Capital for household billing. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-REBAL-2024-087 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,000,000 | 100 |
| $1,000,000 – $2,500,000 | 90 |
| $2,500,000 – $5,250,000 | 70 |
| Above $5,250,000 | 60 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-REBAL-2024-087 charges a household rebalancing fee of 8 bps of the rebalanced notional, with an annual cap of $2,500. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-REBAL-2024-087, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS087 when contacting the Northwind Capital billing desk.
