# Spec Delta

## MODIFIED Requirements

### Requirement: A tool call streams as the protocol's tool events
Each tool call SHALL be streamed as a tool-call start naming the tool, its arguments, a tool-call end, and a
tool-call result, all sharing one tool-call identifier. The start SHALL be emitted before the tool executes.
Arguments and results that reach the client SHALL carry identifiers and summaries only, never free text a user
typed or a document's contents. The only exception is a tool result carried by an activity card, under the
requirement for data cards.

#### Scenario: Tool call lifecycle
- **WHEN** a turn calls a tool
- **THEN** the client receives tool-call start before the tool runs, then its arguments, then end, then the result, under one tool-call id

#### Scenario: A tool call's free text does not travel
- **WHEN** a tool is called with a query or a reason
- **THEN** the arguments the client receives name the identifiers and not the text

## ADDED Requirements

### Requirement: A typed tool result travels as a data card
A tool whose declared result type has no free-text field MAY have its successful result sent to the client as an
activity (`ACTIVITY_SNAPSHOT`).

**Which tools.** The tools SHALL be named on a fixed allow-list in the server, each with its activity type:
- `get_household_portfolio` as `maf-lab/holdings`;
- `get_aum_history` as `maf-lab/aum-history`;
- `list_my_accounts` as `maf-lab/accounts`.

A tool not on the list SHALL NOT produce an activity.

**The event.**
- It SHALL be emitted once per such call, after that call's tool-call result and before the run ends.
- Its message id SHALL be derived from the tool-call id.
- Its content SHALL be the tool's structured result, as the tool's own firm-scoped read produced it.

**When there is no card.** No activity SHALL be emitted for:
- a result that failed;
- a result that is not structured;
- a result the guardrail withheld.

**Only numbers, flags and names.** The content SHALL carry no free text from a user, a document or a record note: only
numbers, flags, enumerations, dates, identifiers and the names of the firm's own accounts and households. The
tool-call result event itself SHALL stay a summary.

#### Scenario: A holdings card
- **WHEN** a turn calls `get_household_portfolio` for A-1043 and it succeeds
- **THEN** after the tool-call result the client receives one activity of type `maf-lab/holdings`, whose content holds
  the account id, the holdings with weights, drift and plan, the total and the currency

#### Scenario: A tool outside the allow-list
- **WHEN** a turn calls `search_documents`
- **THEN** no activity is emitted, and its tool-call result is a summary as before

#### Scenario: A failed or withheld result
- **WHEN** `get_household_portfolio` returns an error, or its result is withheld by the guardrail
- **THEN** no activity is emitted for that call

#### Scenario: Another firm's account
- **WHEN** a firm-b user asks for the portfolio of A-1042
- **THEN** the tool returns its not-found error and no activity is emitted
