# Fee schedule note: NW-REBAL-2024-075

Internal reference: NW-CANARY-7731-FS075. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-REBAL-2024-075 as a household rebalancing fee schedule for advisory households. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-REBAL-2024-075 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $500,000 | 110 |
| $500,000 – $1,250,000 | 95 |
| $1,250,000 – $3,000,000 | 85 |
| Above $3,000,000 | 65 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-REBAL-2024-075 charges a household rebalancing fee of 2 bps of the rebalanced notional, with an annual cap of $5,000. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-REBAL-2024-075, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS075 when contacting the Northwind Capital billing desk.
