# Household Aggregation

## Why Households Matter

Household aggregation lets a firm price a family's relationship as a whole. Instead of evaluating each account against a fee schedule separately, the billing engine sums the billable AUM of all accounts in the household and uses that total to determine the applicable tiers or breakpoints. The resulting fee is then allocated back to the member accounts in proportion to their AUM. Clients generally see a lower effective rate, and the firm avoids penalizing families that spread assets across many small accounts such as IRAs, trusts, and joint accounts.

## Aggregation Rules

### Membership and Dates and Schedule Resolution in a Household

**Membership and Dates.** An account's household membership is effective-dated. For a given billing period, the engine uses the membership in force on the valuation date. If an account joins a household mid-period, it is aggregated for the whole period only when the firm enables the "join as of period start" option; otherwise it is billed standalone for that period and aggregated from the next one.

**Schedule Resolution in a Household.** When a household has its own schedule assignment, all member accounts inherit it unless an account has an explicit override. Overrides are billed separately but can still contribute their AUM to the household total for breakpoint purposes if the firm enables "contribute but bill separately". This is common for accounts billed on a flat retainer that should still help a family reach a breakpoint.

## Allocation

### Splitting the Household Fee

After the household fee is computed, it is allocated to accounts pro rata by billable AUM. Accounts that pay their own fee receive an individual debit; firms may also designate a single paying account, in which case the entire household fee is debited from that account and the other accounts show a zero-fee line referencing the payer. Allocation rounding follows the rules described in the tiered calculation documentation, so account amounts always sum to the household total. The allocation is stored with the run.
