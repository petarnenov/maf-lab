# Fee schedule note: NW-FLAT-2026-032

Internal reference: NW-CANARY-7731-FS032. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-FLAT-2026-032 as a flat schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-032 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $750,000 | 110 |
| $750,000 – $1,500,000 | 90 |
| $1,500,000 – $3,750,000 | 75 |
| $3,750,000 – $6,500,000 | 55 |
| $6,500,000 – $8,500,000 | 40 |
| Above $8,500,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-FLAT-2026-032 is a flat $250 per rebalance event, capped at $5,000 per household per year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-032, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS032 when contacting the Northwind Capital billing desk.
