# Fee schedule note: NW-FLAT-2026-086

Internal reference: NW-CANARY-7731-FS086. Owner: S. Iqbal, Northwind Capital billing operations.

## Overview

Fee schedule NW-FLAT-2026-086 is one of the flat schedules in the Northwind Capital fee schedule catalog. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-086 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $1,500,000 | 90 |
| $1,500,000 – $2,500,000 | 75 |
| $2,500,000 – $3,500,000 | 60 |
| Above $3,500,000 | 40 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-FLAT-2026-086 charges a household rebalancing fee of 3 bps of the rebalanced notional, with an annual cap of $10,000. A rebalance event is recorded when the system detects a client-initiated reallocation between household accounts. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-086, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS086 when contacting the Northwind Capital billing desk.
