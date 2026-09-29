# Proposal

## Why

Asked to rebalance an account, the model currently does the arithmetic itself. It works out the trade per asset class
and the weights after the trades, and presents them as a markdown table. `get_household_portfolio` returns weights and
drift but no trades. So every amount in a rebalance answer is model output, not data, and nothing checks it.

It can also recommend trades nobody needs. For A-1043 every class was within its ±5 % tolerance (US equity 20.6 %
against a 20 % target), and the answer still proposed selling 8,000 $.

## What Changes

- `get_household_portfolio` adds a deterministic rebalance plan, computed by the portfolio server from the same holdings
  it already returns:
  - for each asset class, the trade that brings it to its target weight at the current total: a signed amount in the
    account's currency, positive to buy and negative to sell, plus its side (`buy`, `sell` or `none`);
  - the weight each class would have after the trades;
  - whether each class is outside the tolerance.
- The existing account-level `outsideTolerance` is also exposed as `rebalanceNeeded`, so the plan says plainly whether
  any trade is called for.
- The trades sum to zero, since the plan only moves money between classes. They are rounded to whole currency units,
  and the rounding remainder goes to the largest trade.
- The tool's description tells the model to present the plan's figures rather than compute its own, and to say that no
  rebalance is needed when `rebalanceNeeded` is false.
- Read-only: nothing is executed and nothing is written. The plan is information, as the drift already is.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `portfolio-mcp`: the household portfolio result gains the rebalance plan (trade per class, weight after,
  per-class tolerance, `rebalanceNeeded`).

## Impact

- Code:
  - `src/Maf.Lab.Domain/Portfolio/PortfolioDtos.cs`: `HoldingView` and `HouseholdPortfolio` gain fields. This is
    additive, and the output schema is regenerated from the type.
  - `src/Maf.Lab.Portfolio/Store/PortfolioStore.cs`: the calculation.
  - `src/Maf.Lab.Portfolio/Tools/HouseholdTools.cs`: the description.
- Tests: `PortfolioDomainTests`, covering the plan's arithmetic, the zero sum, rounding and the within-tolerance case.
- Evals: `selection` must stay at its baseline, since no tool is added and routing is untouched. How the answer uses the
  plan is measured in `add-system-prompt-v3`.
- No new tool, no new package, and no change to tenancy: the account is still resolved from the principal's firm.
