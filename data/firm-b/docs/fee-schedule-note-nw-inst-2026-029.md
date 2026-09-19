# Fee schedule note: NW-INST-2026-029

Internal reference: NW-CANARY-7731-FS029. Owner: E. Lindgren, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-INST-2026-029 as a institutional breakpoint schedule for advisory households. It is typically assigned to households that rebalance frequently and hold several custodial accounts. The schedule is billed monthly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $2,500 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-029 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,500,000 | 100 |
| $2,500,000 – $3,750,000 | 85 |
| $3,750,000 – $6,000,000 | 70 |
| $6,000,000 – $8,000,000 | 50 |
| $8,000,000 – $9,000,000 | 35 |
| Above $9,000,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Each household rebalancing event under this fee schedule incurs $500, and the household rebalancing fee is capped at $10,000 per calendar year. A rebalance event is recorded when the system detects a client-initiated reallocation between household accounts. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-029, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS029 when contacting the Northwind Capital billing desk.
