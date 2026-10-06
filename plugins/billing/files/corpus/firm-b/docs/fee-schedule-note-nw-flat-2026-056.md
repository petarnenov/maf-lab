# Fee schedule note: NW-FLAT-2026-056

Internal reference: NW-CANARY-7731-FS056. Owner: K. Petrakis, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-FLAT-2026-056, a flat schedule used by Northwind Capital for household billing. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-056 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 110 |
| $2,750,000 – $4,250,000 | 90 |
| $4,250,000 – $7,000,000 | 80 |
| $7,000,000 – $9,500,000 | 65 |
| $9,500,000 – $10,750,000 | 50 |
| Above $10,750,000 | 35 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-FLAT-2026-056 is a flat $500 per rebalance event, capped at $10,000 per household per year. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-056, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS056 when contacting the Northwind Capital billing desk.
