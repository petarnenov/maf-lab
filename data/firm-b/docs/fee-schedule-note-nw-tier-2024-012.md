# Fee schedule note: NW-TIER-2024-012

Internal reference: NW-CANARY-7731-FS012. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

This note documents fee schedule NW-TIER-2024-012, a tiered schedule used by Northwind Capital for household billing. It is the default for households onboarded through the wealth planning channel. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-TIER-2024-012 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,500,000 | 95 |
| $2,500,000 – $4,750,000 | 75 |
| $4,750,000 – $5,500,000 | 60 |
| $5,500,000 – $6,250,000 | 40 |
| Above $6,250,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-TIER-2024-012 is a flat $750 per rebalance event, capped at $2,500 per household per year. A rebalance event is recorded when the system detects a client-initiated reallocation between household accounts. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-TIER-2024-012, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS012 when contacting the Northwind Capital billing desk.
