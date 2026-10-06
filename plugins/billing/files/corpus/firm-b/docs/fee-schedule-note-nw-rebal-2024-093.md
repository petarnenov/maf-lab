# Fee schedule note: NW-REBAL-2024-093

Internal reference: NW-CANARY-7731-FS093. Owner: K. Petrakis, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-REBAL-2024-093, a household rebalancing fee schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $1,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-REBAL-2024-093 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,000,000 | 95 |
| $1,000,000 – $2,500,000 | 80 |
| $2,500,000 – $3,000,000 | 70 |
| $3,000,000 – $5,000,000 | 55 |
| $5,000,000 – $6,250,000 | 40 |
| Above $6,250,000 | 30 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-REBAL-2024-093 is a flat $500 per rebalance event, capped at $7,500 per household per year. A rebalance event is recorded when the system detects a client-initiated reallocation between household accounts. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-REBAL-2024-093, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS093 when contacting the Northwind Capital billing desk.
