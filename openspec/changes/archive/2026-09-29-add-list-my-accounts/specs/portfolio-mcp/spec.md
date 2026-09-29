# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
