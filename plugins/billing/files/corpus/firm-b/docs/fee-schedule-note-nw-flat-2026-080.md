# Fee schedule note: NW-FLAT-2026-080

Internal reference: NW-CANARY-7731-FS080. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-FLAT-2026-080 as a flat schedule for advisory households. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-080 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,500,000 | 90 |
| $1,500,000 – $3,500,000 | 70 |
| $3,500,000 – $5,250,000 | 50 |
| $5,250,000 – $8,000,000 | 30 |
| $8,000,000 – $10,250,000 | 25 |
| Above $10,250,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $750, and the household rebalancing fee is capped at $5,000 per calendar year. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-080, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS080 when contacting the Northwind Capital billing desk.
