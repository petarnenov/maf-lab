# ACME-TIER-2026 Fee Schedule

## Overview

ACME-TIER-2026 is the custom tiered fee schedule adopted by Acme Wealth Partners for the 2026 fiscal year. It replaces the older ACME-TIER-2022 schedule for all new households and for any household that completes an annual review during 2026. The schedule is tiered, not breakpoint-based: each band of assets is charged its own rate, and the household fee is the sum of the band fees. Internal reference code: ACME-CANARY-4410. The schedule is maintained by the Acme billing desk and may only be edited by users with the FIRM_ADMIN role.

## Tier Table

### Annual Rates by Band

- First $1,000,000 of household AUM: 1.00% annually
- Next $2,000,000 (from $1,000,000 to $3,000,000): 0.80% annually
- Next $5,000,000 (from $3,000,000 to $8,000,000): 0.60% annually
- Assets above $8,000,000: 0.45% annually

Quarterly fees are calculated by dividing the annual band fee by four and prorating for partial quarters based on days under management.

### Minimum Fee

ACME-TIER-2026 carries a minimum annual fee of $2,500 per household. If the tiered calculation produces less than the minimum, the minimum is billed and the difference is recorded as a minimum-fee adjustment line on the invoice.

## Worked Example

A household with $4,500,000 of average quarterly AUM pays 1.00% on the first $1,000,000 ($10,000), 0.80% on the next $2,000,000 ($16,000), and 0.60% on the remaining $1,500,000 ($9,000). The annual fee is $35,000, so the quarterly invoice is $8,750. Because this is below $25,000, the invoice does not require FIRM_ADMIN approval.
