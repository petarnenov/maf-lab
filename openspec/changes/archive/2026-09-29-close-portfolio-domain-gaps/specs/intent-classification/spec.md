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
