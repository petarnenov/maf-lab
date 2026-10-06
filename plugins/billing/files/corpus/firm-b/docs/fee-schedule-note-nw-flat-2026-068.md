# Fee schedule note: NW-FLAT-2026-068

Internal reference: NW-CANARY-7731-FS068. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-FLAT-2026-068, a flat schedule used by Northwind Capital for household billing. It is the default for households onboarded through the wealth planning channel. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-068 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,750,000 | 100 |
| $2,750,000 – $3,750,000 | 85 |
| $3,750,000 – $5,750,000 | 70 |
| $5,750,000 – $7,750,000 | 55 |
| $7,750,000 – $10,250,000 | 40 |
| Above $10,250,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $350, and the household rebalancing fee is capped at $10,000 per calendar year. A rebalance event is recorded when the system detects a drift of more than 5% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-068, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS068 when contacting the Northwind Capital billing desk.
