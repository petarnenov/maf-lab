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
- It SHALL expose the tools `search_portfolio_documents`, `get_household_portfolio` and `get_aum_history`.
- All three tools SHALL be read-only.
- It SHALL run as at least two replicas behind the load balancer.

#### Scenario: Tools listed
- **WHEN** an authenticated client lists the portfolio server's tools
- **THEN** it receives exactly `search_portfolio_documents`, `get_household_portfolio` and `get_aum_history`, each marked read-only

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
- the drift of each asset class;
- the total market value.

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
