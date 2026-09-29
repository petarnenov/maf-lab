# Spec Delta

## MODIFIED Requirements

### Requirement: Portfolio read tools routed within the domains in scope
Tool routing SHALL also ask Jev about `get_household_portfolio`, `get_aum_history` and `list_my_accounts`, in the
same request as the intent. A data question SHALL be routed only among the read tools of the domains in scope. With no
domain verdict, it SHALL be routed among all of them.

A per-account portfolio tool (`get_household_portfolio`, `get_aum_history`) SHALL be routed only when the question
names exactly one account id of the form letter-dash-number. The id SHALL be taken from the question by a fixed
pattern, never from the model.

`list_my_accounts` SHALL be routed with no arguments. It SHALL be routed only when the question names no account id.

Every other routing rule, the write-tool veto included, SHALL be unchanged.

#### Scenario: Holdings of one account
- **WHEN** routing is on and the user asks "Show me the holdings of A-1042", placed in the portfolio domain
- **THEN** the turn is routed to `get_household_portfolio` with account id `A-1042`, and never to a billing tool

#### Scenario: Two accounts
- **WHEN** the user asks "Compare the holdings of A-1042 and A-1043"
- **THEN** the turn is not routed and the reason says the tool needs one account id

#### Scenario: Which accounts
- **WHEN** routing is on and the user asks "Which accounts do I have access to?", placed in the portfolio domain
- **THEN** the turn is routed to `list_my_accounts` with no arguments

#### Scenario: Account list with an account named
- **WHEN** Jev's highest read tool is `list_my_accounts` but the question names A-1042
- **THEN** the turn is not routed and the reason says the account list takes no account id
