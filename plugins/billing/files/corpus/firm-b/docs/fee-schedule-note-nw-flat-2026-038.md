# Fee schedule note: NW-FLAT-2026-038

Internal reference: NW-CANARY-7731-FS038. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-FLAT-2026-038, a flat schedule used by Northwind Capital for household billing. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-038 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $500,000 | 110 |
| $500,000 – $2,500,000 | 90 |
| $2,500,000 – $3,750,000 | 70 |
| $3,750,000 – $4,750,000 | 50 |
| $4,750,000 – $7,250,000 | 40 |
| Above $7,250,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-FLAT-2026-038 is a flat $750 per rebalance event, capped at $7,500 per household per year. A rebalance event is recorded when the system detects a model change that forces a household-wide rebalance. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-038, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS038 when contacting the Northwind Capital billing desk.
