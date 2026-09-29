# Spec Delta

## MODIFIED Requirements

### Requirement: One domain question per domain
The classification request SHALL ask Jev one yes/no question per domain, in the same request as the intent:
- the existing billing question;
- a portfolio question, whose domain is described beside the question;
- a codebase question, whose domain is this lab's own software: its source code, types, methods, files, tests,
  configuration, MCP servers and tools, specifications and design decisions. General programming that is not about
  this system is not in it.

The decision SHALL keep each domain's probability.

The gate that lets a procedural or mixed intent act SHALL use the highest domain probability against the existing
floor. So a portfolio or codebase procedure is acted on as a billing procedure is.

The decision SHALL name the domains **in scope**:
- every domain whose probability reaches a configured scope floor, defaulting to 0.5;
- when the gate passes but no domain reaches the scope floor, the most probable domain alone.

A question with two or more domains in scope SHALL be marked as **crossing** the domain boundary.

#### Scenario: Portfolio procedure
- **WHEN** the user asks "What drift tolerance triggers a rebalance?"
- **THEN** the intent is procedural, portfolio is the only domain in scope, and the question does not cross

#### Scenario: Crossing question
- **WHEN** the user asks "Why did the fee on A-1042 go up this quarter — did its AUM cross a tier?"
- **THEN** both billing and portfolio are in scope and the decision is marked crossing

#### Scenario: Off-domain question
- **WHEN** the user asks "What is the procedure for renewing a passport?"
- **THEN** no domain reaches the gate, nothing is forced, and the reason says the question is outside the domain

#### Scenario: Codebase question, high probability
- **WHEN** the user asks "как в кода се прави идемпотентност на тул?"
- **THEN** codebase's probability reaches the scope floor, codebase is in scope, and the question is not outside the domains

#### Scenario: Codebase question below the scope floor but above the gate
- **WHEN** codebase is the most probable domain, below the scope floor and at or above the gate floor
- **THEN** codebase alone is in scope

#### Scenario: General programming is not the codebase
- **WHEN** the user asks "How do I reverse a linked list in Python?"
- **THEN** no domain is in scope and the question is outside the domains

#### Scenario: Jev unavailable
- **WHEN** the classification request fails or times out
- **THEN** the decision has no domain verdict, as for the other domains, and nothing is forced

## ADDED Requirements

### Requirement: A codebase question searches the codebase
When the codebase is the primary domain in scope, the turn SHALL force `search_codebase` for any intent except
chitchat, because the codebase has no read tools and its search is the only way to answer. A forcing intent (procedural
or mixed) with the codebase and another domain in scope SHALL force both domains' searches. Data routing SHALL never
route to the codebase.

#### Scenario: A "show me" question about code
- **WHEN** the user asks "покажи ми дефиницията на code mcp сървъра" and Jev classifies the intent as data or other with the codebase primary
- **THEN** `search_codebase` is forced

#### Scenario: Small talk is not forced
- **WHEN** the intent is chitchat
- **THEN** nothing is forced, whatever the domain answers
