# Spec Delta

## ADDED Requirements

### Requirement: Portfolio read tools routed within the domains in scope
Tool routing SHALL also ask Jev about `get_household_portfolio` and `get_aum_history`, in the same request as the
intent. A data question SHALL be routed only among the read tools of the domains in scope; with no domain verdict,
among all of them.

A portfolio tool SHALL be routed only when the question names exactly one account id of the form letter-dash-number.
The id SHALL be taken from the question by a fixed pattern, never from the model.

Every other routing rule, the write-tool veto included, SHALL be unchanged.

#### Scenario: Holdings of one account
- **WHEN** routing is on and the user asks "Show me the holdings of A-1042", placed in the portfolio domain
- **THEN** the turn is routed to `get_household_portfolio` with account id `A-1042`, and never to a billing tool

#### Scenario: Two accounts
- **WHEN** the user asks "Compare the holdings of A-1042 and A-1043"
- **THEN** the turn is not routed and the reason says the tool needs one account id

## MODIFIED Requirements

### Requirement: One domain question per domain
The classification request SHALL ask Jev one yes/no question per domain, in the same request as the intent:
- the existing billing question;
- a portfolio question, whose domain is described beside the question.

The decision SHALL keep each domain's probability.

The gate that lets a procedural or mixed intent act SHALL use the highest domain probability against the existing
floor. So a portfolio procedure is acted on as a billing procedure is.

The decision SHALL name the domains **in scope**:
- every domain whose probability reaches a configured scope floor, defaulting to 0.6;
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
