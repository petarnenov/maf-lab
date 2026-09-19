# Fee schedule note: NW-BRK-2025-115

Internal reference: NW-CANARY-7731-FS115. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-BRK-2025-115, a breakpoint schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $1,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-BRK-2025-115 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,750,000 | 110 |
| $1,750,000 – $3,500,000 | 90 |
| $3,500,000 – $6,250,000 | 70 |
| $6,250,000 – $9,000,000 | 55 |
| Above $9,000,000 | 35 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-BRK-2025-115 charges a household rebalancing fee of 3 bps of the rebalanced notional, with an annual cap of $5,000. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-BRK-2025-115, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS115 when contacting the Northwind Capital billing desk.
