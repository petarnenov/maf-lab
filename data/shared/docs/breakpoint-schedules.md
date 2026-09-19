# Breakpoint Schedules

## Definition

A breakpoint schedule selects one annual rate for the entire billable AUM based on which band the total falls into. Bands are defined by their lower thresholds, called breakpoints. For example, a schedule may charge 1.10% below $500,000, 0.90% from $500,000 up to $2,000,000, and 0.70% at or above $2,000,000. Unlike a tiered schedule, the chosen rate applies to every dollar, which makes the calculation simple but introduces a jump in the fee when a breakpoint is crossed. Breakpoints are listed in ascending order on the schedule.

## Breakpoint Evaluation

### Which AUM Is Used and Boundary Handling

**Which AUM Is Used.** The breakpoint is evaluated on the aggregated AUM of the billing unit. When household aggregation is enabled, the billing unit is the household and the combined AUM of all member accounts determines the rate. When it is disabled, each account is evaluated on its own balance. The valuation date used for breakpoint evaluation is the same as for the fee calculation, so a run never mixes balances from different dates.

**Boundary Handling.** A value exactly equal to a breakpoint falls into the higher band, meaning it receives the lower rate. This rule is documented on client agreements and should be applied consistently in any spreadsheet a firm uses to double-check invoices.

## Client Communication

### Explaining Step Changes

Because the full balance moves to a new rate at a breakpoint, a small market movement can change the fee by more than the movement itself. Advisors should explain this effect when onboarding clients near a threshold. Some firms add a grace band so that a household that falls just below a breakpoint keeps the lower rate for one additional period; this is modeled as a schedule option called breakpoint retention and is shown on the invoice as a note.
