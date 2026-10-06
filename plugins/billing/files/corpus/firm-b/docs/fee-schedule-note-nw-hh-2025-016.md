# Fee schedule note: NW-HH-2025-016

Internal reference: NW-CANARY-7731-FS016. Owner: J. Moreau, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-HH-2025-016 as a household aggregated tiered schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-HH-2025-016 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $750,000 | 105 |
| $750,000 – $2,500,000 | 85 |
| $2,500,000 – $5,000,000 | 65 |
| $5,000,000 – $7,750,000 | 55 |
| $7,750,000 – $9,250,000 | 35 |
| Above $9,250,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

The household rebalancing fee under NW-HH-2025-016 is a flat $350 per rebalance event, capped at $2,500 per household per year. A rebalance event is recorded when the system detects a client-initiated reallocation between household accounts. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-HH-2025-016, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS016 when contacting the Northwind Capital billing desk.
