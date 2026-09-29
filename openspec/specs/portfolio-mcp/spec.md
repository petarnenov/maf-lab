# portfolio-mcp Specification

## Purpose
The portfolio domain's own MCP server: its documentation, searched through its own collection by the one tenant-scoped
query method, and read tools over household portfolios keyed by billing's account ids — the second side of the domain
boundary a question can cross.

## Requirements

### Requirement: Portfolio MCP server
The system SHALL run a second MCP server, `mcp-portfolio`:
- It SHALL speak the same protocol (Streamable HTTP, stateless) and require the same user bearer token as
  `mcp-retrieval`.
- It SHALL derive the tenant from the token's principal only.
- It SHALL expose the tools `search_portfolio_documents`, `get_household_portfolio`, `get_aum_history` and
  `list_my_accounts`.
- All four tools SHALL be read-only.
- It SHALL run as at least two replicas behind the load balancer.

#### Scenario: Tools listed
- **WHEN** an authenticated client lists the portfolio server's tools
- **THEN** it receives exactly `search_portfolio_documents`, `get_household_portfolio`, `get_aum_history` and `list_my_accounts`, each marked read-only

#### Scenario: Unauthenticated call
- **WHEN** a client calls the portfolio server without a valid bearer token
- **THEN** the request is rejected with 401

### Requirement: Portfolio documentation RAG tool
`search_portfolio_documents`:
- SHALL search only the portfolio corpus, indexed into its own collection and BM25 vocabulary.
- SHALL go through the one tenant-scoped query method: the caller's firm plus shared, never another firm.
- SHALL return the same result shape as `search_documents`: snippets with document, section and source path, never a
  synthesized answer.
- SHALL use the same hybrid search, relevance gate and reranker.
- SHALL return retrieval diagnostics in the result `_meta` when the caller asks for them.

#### Scenario: Portfolio question answered from portfolio docs
- **WHEN** a firm-a user searches "what drift tolerance triggers a rebalance"
- **THEN** snippets come from portfolio documents of firm-a or shared, and none from the billing collection

#### Scenario: Tenant isolation holds in the second collection
- **WHEN** a firm-b user searches for a phrase that only appears in a firm-a portfolio document
- **THEN** no firm-a document is returned

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

### Requirement: Accounts of the logged-in user
`list_my_accounts` SHALL return the accounts the calling principal can access:
- Access SHALL be the same firm scope that the per-account read tools apply: every account of the principal's firm,
  and no account of another firm.
- It SHALL take no arguments. In particular, no tenant, firm, user or advisor argument.
- Each row SHALL carry the account id, name, household id, model portfolio and currency.
- Rows SHALL be ordered by account id.
- The result SHALL carry the number of rows.
- It SHALL never return a record's internal note or its holdings.
- A firm with no accounts SHALL get an empty list, not an error.

Every account id it returns SHALL be accepted by `get_household_portfolio` and `get_aum_history` for the same caller.

#### Scenario: Advisor of firm-a lists accounts
- **WHEN** a firm-a user calls `list_my_accounts`
- **THEN** the result lists A-1042, A-1043 and A-1044 in that order, with their households, and a count of 3

#### Scenario: No other firm's accounts
- **WHEN** a firm-b user calls `list_my_accounts`
- **THEN** the result lists only B-200 and B-201, and no firm-a or firm-c account

#### Scenario: No note in the result
- **WHEN** any user calls `list_my_accounts`
- **THEN** no text from any record's internal note appears in the result
