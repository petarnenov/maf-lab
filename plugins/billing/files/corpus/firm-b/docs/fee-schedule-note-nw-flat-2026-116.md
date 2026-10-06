# Fee schedule note: NW-FLAT-2026-116

Internal reference: NW-CANARY-7731-FS116. Owner: M. Okafor, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-FLAT-2026-116, a flat schedule used by Northwind Capital for household billing. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. No minimum annual fee applies to this schedule. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-116 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,500,000 | 95 |
| $2,500,000 – $3,250,000 | 80 |
| $3,250,000 – $5,750,000 | 65 |
| Above $5,750,000 | 55 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-FLAT-2026-116 is a flat $150 per rebalance event, capped at $7,500 per household per year. A rebalance event is recorded when the system detects a drift of more than 3% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-116, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS116 when contacting the Northwind Capital billing desk.
