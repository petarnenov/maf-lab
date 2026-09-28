# Spec Delta

## ADDED Requirements

### Requirement: Tools from every domain server
For each turn, the agent SHALL read the tools of every configured MCP server with the caller's bearer token and offer
their union.

Every tool SHALL be known by the domain and the server that own it. A tool name offered by two servers SHALL be kept
from the first and dropped from the second, with a log entry.

The billing server failing SHALL fail the turn, as before. Another domain's server that cannot be reached SHALL leave
its tools out of the turn without failing it.

A confirmation SHALL be sent to the server that owns the tool.

#### Scenario: Both domains offered
- **WHEN** both servers are up and a turn starts
- **THEN** the prompt event lists the billing tools and the portfolio tools, each with its domain

#### Scenario: Portfolio server down
- **WHEN** the portfolio server cannot be reached
- **THEN** the turn runs with the billing tools only, and the prompt event shows only billing as offered

### Requirement: Retrieval forced in every domain in scope
When a forcing intent is acted on, the agent SHALL force the search tool of every domain in scope that is offered.
With emulation on, those calls SHALL be issued together, on the model's behalf, before its first call. The model SHALL
then answer with every result in context, free to call further tools.

#### Scenario: Crossing question forces both searches
- **WHEN** a procedural question is in scope for billing and portfolio
- **THEN** `search_documents` and `search_portfolio_documents` are both called before the model's first call
