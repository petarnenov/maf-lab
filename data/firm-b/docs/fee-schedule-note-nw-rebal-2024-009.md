# Fee schedule note: NW-REBAL-2024-009

Internal reference: NW-CANARY-7731-FS009. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-REBAL-2024-009, a household rebalancing fee schedule used by Northwind Capital for household billing. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-REBAL-2024-009 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 110 |
| $2,750,000 – $4,500,000 | 90 |
| $4,500,000 – $6,750,000 | 70 |
| $6,750,000 – $9,250,000 | 55 |
| $9,250,000 – $11,500,000 | 35 |
| Above $11,500,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-REBAL-2024-009 is a flat $500 per rebalance event, capped at $10,000 per household per year. A rebalance event is recorded when the system detects a drift of more than 5% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-REBAL-2024-009, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS009 when contacting the Northwind Capital billing desk.
