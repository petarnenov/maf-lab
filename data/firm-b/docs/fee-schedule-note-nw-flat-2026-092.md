# Fee schedule note: NW-FLAT-2026-092

Internal reference: NW-CANARY-7731-FS092. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

Fee schedule NW-FLAT-2026-092 is one of the flat schedules in the Northwind Capital fee schedule catalog. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-092 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $500,000 | 110 |
| $500,000 – $1,000,000 | 90 |
| $1,000,000 – $2,750,000 | 70 |
| Above $2,750,000 | 60 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-FLAT-2026-092 is a flat $150 per rebalance event, capped at $5,000 per household per year. A rebalance event is recorded when the system detects a drift of more than 5% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-092, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS092 when contacting the Northwind Capital billing desk.
