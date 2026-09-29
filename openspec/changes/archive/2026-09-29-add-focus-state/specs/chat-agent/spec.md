# Spec Delta

## ADDED Requirements

### Requirement: An account-less portfolio question is about the account in focus
**How the focus is set.** A conversation's account in focus SHALL be set by the server:
- A successful `get_household_portfolio` or `get_aum_history` read for one account SHALL set the focus to that account.
- `list_my_accounts`, a failed read and a read withheld by the guard SHALL NOT change it.

**What the model is told.**
- When a turn starts with an account in focus, the model SHALL be told the account's id, and told that a question
  naming no account is about that account. The id SHALL be the only interpolated content of that note.
- On the turn the user clears a focus the conversation had, the model SHALL be told, right before the question, not to
  assume an account from earlier in the conversation, and to ask which account a question that names none is about.
  On that turn a holdings or AUM read SHALL NOT be made unless the question names an account; the model SHALL get a
  tool result telling it to ask instead.

**Data routing.** When a turn is routed to a portfolio read tool other than `list_my_accounts`, and the question names
no account id, the routed call SHALL use the account in focus. Without a focus, the turn SHALL NOT be routed, as before.
A question that names an account SHALL always use the named account.

**Tenancy.** The focus SHALL never widen what the user can read. Every portfolio read SHALL remain scoped to the
caller's firm by the principal, whatever the focus says.

**Tracing.** The trace SHALL record the focus a turn started with and its source: stored, sent by the client, or none.
It SHALL also record any change the turn made to it.

#### Scenario: A follow-up about the same account
- **WHEN** the previous turn read A-1043's portfolio, and the user now asks "а AUM-ът по тримесечия?"
- **THEN** the AUM read is for A-1043, whether the turn is routed or the model chooses the call

#### Scenario: A question that names another account
- **WHEN** A-1043 is in focus and the user asks "What does A-1042 hold?"
- **THEN** the read is for A-1042, and the focus moves to A-1042

#### Scenario: No focus
- **WHEN** no account is in focus and the user asks "What does it hold?"
- **THEN** the turn is not routed, and the model chooses what to do as before

#### Scenario: The user clears the focus
- **WHEN** A-1044 was in focus, the user clears it, and asks "What does it hold?"
- **THEN** the turn is not routed, the model is told to ask which account is meant, and a holdings or AUM read the model
  attempts without the question naming an account is not made

#### Scenario: A list does not move the focus
- **WHEN** A-1043 is in focus and the user asks "Which accounts do I have?"
- **THEN** the accounts are listed and A-1043 stays in focus
