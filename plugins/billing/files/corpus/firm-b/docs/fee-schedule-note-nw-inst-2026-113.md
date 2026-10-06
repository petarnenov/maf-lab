# Fee schedule note: NW-INST-2026-113

Internal reference: NW-CANARY-7731-FS113. Owner: J. Moreau, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-INST-2026-113, a institutional breakpoint schedule used by Northwind Capital for household billing. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-113 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,750,000 | 110 |
| $1,750,000 – $2,500,000 | 100 |
| $2,500,000 – $4,750,000 | 85 |
| $4,750,000 – $5,250,000 | 75 |
| $5,250,000 – $6,000,000 | 65 |
| Above $6,000,000 | 45 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-INST-2026-113 is a flat $750 per rebalance event, capped at $10,000 per household per year. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-113, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS113 when contacting the Northwind Capital billing desk.
