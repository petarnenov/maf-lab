# Design

## Context

- `PortfolioStore.Portfolio` sums the holdings and derives the actual weight (1 dp) and drift per class. It sets
  `OutsideTolerance` when any |drift| is above `DriftTolerancePct`.
- `HouseholdTools.GetPortfolio` returns the DTO as structured content, with `OutputSchemaType = typeof(HouseholdPortfolio)`.
  The schema follows the type.
- Seed targets add up to 100 % per account; `PortfolioDomainTests` asserts this for the seed.

## Goals / Non-Goals

**Goals:**
- Trades and weights-after the model can quote rather than compute.
- A plain yes/no on whether any trade is needed.

**Non-Goals:**
- No trade execution, orders, lots, tax or transaction costs.
- No minimum trade size, and no "rebalance only the drifted classes" variant. The plan brings every class to target,
  and presenting only the out-of-tolerance classes is the model's wording, from the per-class flag.
- No new tool: the plan rides on the result the model already reads for "has it drifted".

## Decisions

1. **Extend `get_household_portfolio`, do not add `plan_rebalance`.**
   - A new tool would add a selection target, a Jev routing question and eval rows for data the portfolio call has
     already fetched.
   - *Alternative:* a separate tool. Rejected, because one read returning both the drift and what fixes it is simpler
     for the model.

2. **The fields:**
   - `HoldingView` gains `OutsideTolerance` (bool), `TradeToTarget` (decimal, signed), `TradeSide` (`buy`, `sell` or
     `none`) and `WeightAfterPct` (decimal, 1 dp).
   - `HouseholdPortfolio` gains `RebalanceNeeded` (bool), equal to `OutsideTolerance`. The old field is kept for
     compatibility; the new name says what it means for a plan.
   - All fields are numbers, enums or bools. There is no free text, which the activity card (add-activity-cards)
     relies on.

3. **The arithmetic, in `decimal`:**
   - `exact_i = total × target_i / 100 − value_i`, then `trade_i = Math.Round(exact_i, 0, MidpointRounding.ToEven)`.
   - `residual = −Σ trade_i` is added to the trade with the largest |exact_i|; ties go to the first in holding order.
   - `weightAfter_i = round((value_i + trade_i) / total × 100, 1)`.
   - With targets summing to 100, `Σ exact_i = 0`, so the residual is only rounding: at most ±(n/2) units.
   - If the targets do not sum to 100, the plan is still computed and still nets to zero. Weights-after then differ
     from targets by the gap, which is the truthful result.
   - `total == 0`: all trades 0, sides `none`, weights-after 0.

4. **The description** adds that the result includes a rebalance plan: present its trades and weights-after, do not
   recompute them, and if `rebalanceNeeded` is false say that no rebalance is needed, while the distance to target may
   still be mentioned.

## Risks / Trade-offs

- [The model may still recompute or restate] → That is exactly what `add-system-prompt-v3` addresses and measures.
- [A larger result] → Four fields per class and one per account: a few dozen tokens.
