# Spec Delta

## MODIFIED Requirements

### Requirement: Tools from every domain server
For each turn, the agent SHALL read the tools of the MCP servers of the turn's **selected domains** with the caller's
bearer token and offer their union. The selected domains SHALL be:
- the domains in scope of the turn's classification, when it has any;
- otherwise, when the turn has a domain verdict with none in scope, the domains stored for the conversation by its last
  turn that had domains in scope (a follow-up keeps its conversation's tools);
- otherwise every configured domain, as when the classification gave no domain verdict at all.

A turn with domains in scope SHALL store them as the conversation's domains. A server whose domain is not selected
SHALL NOT be contacted during that turn.

A server MAY be configured with a list of the tools it offers the agent. Its other tools SHALL not be offered.

Every tool SHALL be known by the domain and the server that own it. A tool name offered by two servers SHALL be kept
from the first and dropped from the second, with a log entry.

A server that cannot be reached SHALL leave its domain's tools out of the turn without failing it; a turn SHALL fail
only when every server of its selected domains cannot be reached. No server is first: billing's is configured and
treated like any other (introduce-plugins 4.6).

A confirmation SHALL be sent to the server that owns the tool, whatever domains the current turn selected.

#### Scenario: Both domains offered
- **WHEN** both billing and portfolio are in scope and a turn starts
- **THEN** the prompt event lists the billing tools and the portfolio tools, each with its domain

#### Scenario: Portfolio server down
- **WHEN** the portfolio server cannot be reached on a turn that selected billing and portfolio
- **THEN** the turn runs with the billing tools only, and the prompt event shows only billing as offered

#### Scenario: A code question sees only the codebase
- **WHEN** only the codebase is in scope
- **THEN** the turn offers `search_codebase` alone, and neither the billing nor the portfolio server is contacted

#### Scenario: A follow-up keeps the conversation's tools
- **WHEN** the previous turn had portfolio in scope and the user asks "and for A-1043?", which Jev puts in no domain
- **THEN** the turn offers the portfolio tools

#### Scenario: No verdict loads everything
- **WHEN** Jev is unavailable
- **THEN** the turn offers the tools of every configured server

#### Scenario: Only the listed tools of the codebase server
- **WHEN** the codebase server offers search_codebase and ask_codebase and is configured to offer search_codebase
- **THEN** the agent is offered search_codebase and not ask_codebase

#### Scenario: One of two selected servers is down
- **WHEN** a turn selected billing and codebase and the billing server cannot be reached
- **THEN** the turn runs with the codebase tools, and the trace lists billing as unavailable
