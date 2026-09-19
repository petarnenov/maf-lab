# Fee schedule note: NW-HH-2025-082

Internal reference: NW-CANARY-7731-FS082. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-HH-2025-082 as a household aggregated tiered schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $1,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-082 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $500,000 | 100 |
| $500,000 – $2,250,000 | 85 |
| $2,250,000 – $3,750,000 | 70 |
| $3,750,000 – $6,500,000 | 50 |
| $6,500,000 – $8,750,000 | 40 |
| Above $8,750,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-HH-2025-082 charges a household rebalancing fee of 2 bps of the rebalanced notional, with an annual cap of $10,000. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-082, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS082 when contacting the Northwind Capital billing desk.
