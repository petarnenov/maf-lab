# Fee schedule note: NW-FLAT-2026-050

Internal reference: NW-CANARY-7731-FS050. Owner: S. Iqbal, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-FLAT-2026-050, a flat schedule used by Northwind Capital for household billing. It is the default for households onboarded through the wealth planning channel. The schedule is billed quarterly in advance and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-050 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,250,000 | 110 |
| $2,250,000 – $4,750,000 | 100 |
| $4,750,000 – $6,250,000 | 90 |
| Above $6,250,000 | 75 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-FLAT-2026-050 charges a household rebalancing fee of 2 bps of the rebalanced notional, with an annual cap of $5,000. A rebalance event is recorded when the system detects a drift of more than 3% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-050, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS050 when contacting the Northwind Capital billing desk.
