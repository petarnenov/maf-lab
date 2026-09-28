# Spec Delta

## ADDED Requirements

### Requirement: One domain question per domain
The classification request SHALL ask Jev one yes/no question per domain, in the same request as the intent:
- the existing billing question;
- a portfolio question, whose domain is described beside the question.

The decision SHALL keep each domain's probability.

The gate that lets a procedural or mixed intent act SHALL use the highest domain probability against the existing
floor. So a portfolio procedure is acted on as a billing procedure is.

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

### Requirement: Billing routing only in the billing domain
Routing a data question to a billing read tool SHALL additionally require billing to be in scope. A data question
about the portfolio alone SHALL NOT be routed, and the reason SHALL say so.

#### Scenario: Portfolio data question
- **WHEN** the user asks "Show me the holdings of A-1042"
- **THEN** no billing tool is routed and the model chooses the tool
