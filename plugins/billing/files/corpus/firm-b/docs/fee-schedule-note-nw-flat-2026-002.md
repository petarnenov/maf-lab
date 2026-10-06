# Fee schedule note: NW-FLAT-2026-002

Internal reference: NW-CANARY-7731-FS002. Owner: T. Varga, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-FLAT-2026-002, a flat schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-002 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,750,000 | 105 |
| $1,750,000 – $4,250,000 | 90 |
| $4,250,000 – $5,750,000 | 75 |
| $5,750,000 – $7,250,000 | 55 |
| Above $7,250,000 | 35 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $150, and the household rebalancing fee is capped at $10,000 per calendar year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-002, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS002 when contacting the Northwind Capital billing desk.
