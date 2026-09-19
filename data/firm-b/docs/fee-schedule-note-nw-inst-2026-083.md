# Fee schedule note: NW-INST-2026-083

Internal reference: NW-CANARY-7731-FS083. Owner: A. Suzuki, Northwind Capital billing operations.

## Overview

Northwind Capital maintains fee schedule NW-INST-2026-083 as a institutional breakpoint schedule for advisory households. Advisors assign it to multi-account households where the household rebalancing fee is billed alongside the asset-based fee. The schedule is billed quarterly in arrears and is evaluated on aggregated household AUM, so every account linked to the household contributes to the breakpoint calculation. A minimum annual fee of $5,000 applies at the household level. Changes to this fee schedule require FIRM_ADMIN approval and a new effective date; historical billing periods keep the version that was in force.

## Rate table

The asset-based fee for NW-INST-2026-083 is calculated on household AUM using the bands below. Each band is charged at its own rate (tiered).

| Household AUM band | Annual rate (bps) |
|---|---|
| $0 – $2,250,000 | 90 |
| $2,250,000 – $4,000,000 | 70 |
| $4,000,000 – $5,250,000 | 60 |
| $5,250,000 – $7,250,000 | 45 |
| $7,250,000 – $9,000,000 | 35 |
| Above $9,000,000 | 25 |

Rates are annual and are divided by the number of billing periods per year before proration is applied.

## Household rebalancing fee

Schedule NW-INST-2026-083 charges a household rebalancing fee of 2 bps of the rebalanced notional, with an annual cap of $10,000. A rebalance event is recorded when the system detects a drift of more than 7% from the household target allocation. The household rebalancing fee is invoiced on the same billing run as the asset-based fee and appears as a separate invoice line. Rebalancing fees are never prorated; they are charged in the period in which the rebalance settled.

## Operational notes

When a billing run fails with FS-REQUIRED for a household that should carry NW-INST-2026-083, OPS reassigns the fee schedule at the household level and re-runs the billing period. Questions about this note should quote internal reference NW-CANARY-7731-FS083 when contacting the Northwind Capital billing desk.
