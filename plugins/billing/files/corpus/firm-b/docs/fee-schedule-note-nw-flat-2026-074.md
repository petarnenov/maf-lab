# Fee schedule note: NW-FLAT-2026-074

Internal reference: NW-CANARY-7731-FS074. Owner: L. Brennan, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-FLAT-2026-074 as a flat schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-FLAT-2026-074 is calculated on household AUM using the bands below. The flat schedule still records bands for reporting, but the top rate is applied to the whole balance.

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,500,000 | 105 |
| $2,500,000 – $3,250,000 | 90 |
| $3,250,000 – $4,500,000 | 75 |
| $4,500,000 – $7,000,000 | 55 |
| Above $7,000,000 | 35 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $500, and the household rebalancing fee is capped at $5,000 per calendar year. A rebalance event is recorded when the system detects a quarterly calendar rebalance requested by the advisor. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-FLAT-2026-074, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS074 when contacting the Northwind Capital billing desk.
