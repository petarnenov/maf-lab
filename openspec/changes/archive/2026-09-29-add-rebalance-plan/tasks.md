# Tasks

## 1. The plan

- [x] 1.1 In `PortfolioDomainTests`, add failing tests:
  - the spec's A-1043-shaped case (−8,000 / −1,000 / 0 / +9,000, weights after 20 / 10 / 60 / 10,
    `RebalanceNeeded` false);
  - a drifted case where one class is outside the tolerance and `RebalanceNeeded` is true;
  - fractional exact trades that round to whole units summing to exactly 0, with the remainder on the largest trade;
  - a zero-total account.

  Verify that they fail.
- [x] 1.2 Add `OutsideTolerance`, `TradeToTarget`, `TradeSide` and `WeightAfterPct` to `HoldingView`, and `RebalanceNeeded` to `HouseholdPortfolio`; compute them in `PortfolioStore.Portfolio` as in design §3. Verify the tests from 1.1 pass, along with the existing portfolio tests.
- [x] 1.3 Update `get_household_portfolio`'s description as in design §4. Verify with a test that the tool's output schema lists the new fields and still has no free-text field besides the names it had.

## 2. Verification

- [x] 2.1 Run `make test` and `make lint`; both green.
- [x] 2.2 Rebuild with `make`, then ask "Препоръчай ребалансиране за A-1043" at http://localhost:7171/chat as firm-a. Verify that the answer's figures match the tool result in the monitor and that it says no rebalance is needed. Then run `make eval SUITE=selection` and verify it stays at its baseline.
- [x] 2.3 Add a DECISIONS entry: the plan's rule, the rounding, and why it extends the existing tool. Then run `openspec validate add-rebalance-plan --strict`; valid.
