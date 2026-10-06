# Fee schedule note: NW-INST-2026-053

Internal reference: NW-CANARY-7731-FS053. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-INST-2026-053, a institutional breakpoint schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-053 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,500,000 | 105 |
| $1,500,000 – $4,250,000 | 85 |
| $4,250,000 – $5,750,000 | 70 |
| $5,750,000 – $7,750,000 | 50 |
| $7,750,000 – $8,750,000 | 40 |
| Above $8,750,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-INST-2026-053 charges a household rebalancing fee of 8 bps of the rebalanced notional, with an annual cap of $5,000. A rebalance event is recorded when the system detects a drift of more than 3% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-053, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS053 when contacting the Northwind Capital billing desk.
