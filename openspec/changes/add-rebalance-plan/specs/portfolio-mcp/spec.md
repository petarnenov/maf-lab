# Spec Delta

## MODIFIED Requirements

### Requirement: Household portfolio read tools
`get_household_portfolio` SHALL return, for one account of the caller's firm:
- the household;
- the model portfolio;
- holdings by asset class, with the target and actual weights;
- the drift of each asset class, and whether that drift is outside the model's tolerance;
- the total market value;
- a rebalance plan, and whether any rebalance is needed.

The rebalance plan SHALL be computed by the server from the holdings it returns:
- **The trade.** For each asset class, the trade SHALL be the amount that brings the class to its target weight at the
  current total. It is signed in the account's currency, positive to buy and negative to sell, and carries a side of
  `buy`, `sell` or `none`.
- **The weight after.** For each asset class, the plan SHALL give the weight it would have after the trades.
- **Rounding.** Trades SHALL be rounded to whole currency units, and SHALL sum to exactly zero. Any rounding remainder
  is applied to the trade with the largest absolute amount.
- **Whether it is needed.** A rebalance SHALL be reported as needed exactly when at least one asset class is outside
  the tolerance. The trades SHALL be returned either way, so that the distance to target is visible.

The plan SHALL be information only. Nothing SHALL be executed or written.

`get_aum_history` SHALL return that account's quarter-end AUM valuations, oldest first, with the change from each
quarter to the next.

Both tools:
- SHALL use the account ids of the billing domain.
- SHALL answer an account of another firm exactly as an unknown account.
- SHALL never return a record's internal note.

#### Scenario: Holdings of an own account
- **WHEN** a firm-a user asks for the portfolio of A-1042
- **THEN** the result lists its holdings, weights, drift and total value

#### Scenario: Another firm's account
- **WHEN** a firm-b user asks for the AUM history of A-1042
- **THEN** the tool returns the same not-found error it returns for an account that does not exist

#### Scenario: A plan within tolerance
- **WHEN** an account totalling 1,300,000 holds US equity 268,000, international equity 131,000, core bonds 780,000
  and cash 121,000 against targets of 20 %, 10 %, 60 % and 10 %, with a 5 % tolerance
- **THEN** the trades are −8,000, −1,000, 0 and +9,000, the weights after are 20 %, 10 %, 60 % and 10 %, no class is
  outside the tolerance, and a rebalance is reported as not needed

#### Scenario: A plan outside tolerance
- **WHEN** one asset class has drifted further from its target than the tolerance
- **THEN** that class is marked outside the tolerance, a rebalance is reported as needed, and the trades bring every
  class to its target

#### Scenario: Trades net to zero after rounding
- **WHEN** the exact trades have fractional currency amounts
- **THEN** every trade is a whole amount and the trades sum to exactly zero
