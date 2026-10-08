# Spec Delta

## MODIFIED Requirements

### Requirement: One domain question per domain
The classification request SHALL ask Jev one yes/no question per domain, in the same request as the intent:
- the existing billing question;
- a portfolio question, whose domain is described beside the question;
- a codebase question, whose domain is this lab's own software: its source code, types, methods, files, tests,
  configuration, MCP servers and tools, specifications and design decisions. General programming that is not about
  this system is not in it;
- a Bulgarian history question, whose domain is the history of Bulgaria from antiquity to the present day — its states,
  rulers, wars, uprisings, liberation and unification, culture and religion — asked about in any language. What is
  not in it SHALL be stated descriptively, never by a label that merely contains the word "history": how an account's
  assets under management or market value changed from one quarter to the next; the user's own past conversations
  with this assistant; the billing runs that ran before and how they ended; the commits and changes made to this
  lab's source code.

The decision SHALL keep each domain's probability.

The gate that lets a procedural or mixed intent act SHALL use the highest domain probability against the existing
floor. So a portfolio, codebase or Bulgarian history procedure is acted on as a billing procedure is.

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

#### Scenario: Bulgarian history question, high probability
- **WHEN** the user asks "Кога е било Априлското въстание и защо се проваля?"
- **THEN** the request carries `in_bulgarian_history` beside the other three domain questions, its probability reaches
  the scope floor, bulgarian-history alone is in scope, and the question is not outside the domains

#### Scenario: Bulgarian history question below the scope floor but above the gate
- **WHEN** bulgarian-history is the most probable domain, below the scope floor and at or above the gate floor
- **THEN** bulgarian-history alone is in scope

#### Scenario: An account's AUM history is the portfolio's, not Bulgaria's
- **WHEN** the user asks "What is the AUM history of A-1042 over the last four quarters?"
- **THEN** portfolio is in scope and bulgarian-history is not

#### Scenario: Past billing runs and past conversations are not history
- **WHEN** the user asks "Which runs failed last quarter?" or "Show me my earlier conversations with you"
- **THEN** bulgarian-history is not in scope

#### Scenario: The code's history is the codebase's
- **WHEN** the user asks "What changed in TenantScopedSearch in the last commits?"
- **THEN** codebase is the most probable domain and bulgarian-history is not in scope

#### Scenario: Jev unavailable
- **WHEN** the classification request fails or times out
- **THEN** the decision has no domain verdict, as for the other domains, and nothing is forced

## ADDED Requirements

### Requirement: A Bulgarian history question searches its corpus
When bulgarian-history is the primary domain in scope, the turn SHALL force `search_bulgarian_history` for any intent
except chitchat: the domain has no read tools, so its search is the only way to answer, and a history question often
reads as data ("who was Simeon I") or matches no intent. A forcing intent (procedural or mixed) with bulgarian-history
and another domain in scope SHALL force both domains' searches. Data routing SHALL never route to bulgarian-history.
The same rule SHALL hold for every domain that has a search and no read tools, so the codebase and bulgarian-history
share one rule rather than one branch each.

#### Scenario: A "who was" question
- **WHEN** the user asks "Кой е Симеон I?" and Jev classifies the intent as data or other with bulgarian-history primary
- **THEN** `search_bulgarian_history` is forced before the model's first call

#### Scenario: Small talk is not forced
- **WHEN** the intent is chitchat
- **THEN** nothing is forced, whatever the domain answers

#### Scenario: A data question about history is not routed
- **WHEN** the intent is data, bulgarian-history alone is in scope and tool routing is on
- **THEN** no read tool is routed, the reason says no read tool belongs to a domain in scope, and the search is forced instead

#### Scenario: Jev puts the question in no domain
- **WHEN** every domain's probability is below the gate on the first turn of a conversation
- **THEN** nothing is forced and the fixed out-of-scope reply is given, as today
