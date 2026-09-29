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

The billing server failing SHALL fail a turn that selected billing, as before. Another domain's server that cannot be
reached SHALL leave its tools out of the turn without failing it.

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

## ADDED Requirements

### Requirement: The out-of-scope reply names the codebase
The fixed reply to a question outside every domain SHALL say that the assistant helps with the firm's billing and
portfolios and with questions about this lab's code, in the language of the question.

#### Scenario: Reply in Bulgarian
- **WHEN** a Bulgarian question is outside every domain
- **THEN** the reply, in Bulgarian, names billing, portfolios and the lab's code

### Requirement: Code search results are sources
The sources of a turn that called `search_codebase` SHALL be its snippets. Each SHALL carry its path, line range,
symbol, language and the kind `code`, beside the fields every source has. The guard SHALL screen each snippet. The
answer check SHALL read each snippet, and the stored conversation SHALL return the sources with those fields.

#### Scenario: Stored and restored
- **WHEN** a turn answered from `search_codebase` is reopened from history
- **THEN** its sources carry path, lines and symbol as they did live
