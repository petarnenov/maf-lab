# Spec Delta

## ADDED Requirements

### Requirement: The account in focus travels as shared state
A run SHALL carry the conversation's account in focus as AG-UI shared state, of the shape
`{ focus: { accountId } | null }`.

**Server to client.**
- The server SHALL emit a `STATE_SNAPSHOT` with the state the turn starts with, right after the run starts.
- It SHALL emit another whenever a tool read changes the focus during the run.

**Client to server.**
- The client MAY send its current state as `RunAgentInput.state`.
- The server SHALL accept a sent `focus.accountId` only when that id appeared in a data card this conversation showed.
  Such an id counts as offered.
- The server SHALL accept `focus: null` as clearing the focus.
- Any other value SHALL be ignored, the stored focus SHALL stand, and the trace SHALL record the rejection without the
  sent value.
- When no state is sent, the stored focus SHALL be used.

The state SHALL carry an account id only: no name, no holdings and no text from the user or the model.

#### Scenario: State at the start of a run
- **WHEN** a conversation whose focus is A-1043 starts a run
- **THEN** the client receives `STATE_SNAPSHOT { focus: { accountId: "A-1043" } }` right after `RUN_STARTED`

#### Scenario: A read moves the focus
- **WHEN** a turn in that conversation reads the portfolio of A-1042 successfully
- **THEN** the client receives a `STATE_SNAPSHOT` with `A-1042` in focus before the run ends

#### Scenario: The client picks an account it was shown
- **WHEN** the client sends `state: { focus: { accountId: "A-1044" } }` and A-1044 was listed in an accounts card of
  this conversation
- **THEN** the turn runs with A-1044 in focus, and the starting snapshot says so

#### Scenario: The client sends an account it was never shown
- **WHEN** the client sends `state: { focus: { accountId: "B-200" } }` in a firm-a conversation that never showed B-200
- **THEN** the sent focus is ignored, the stored focus stands, and the trace records a rejected focus without the id

#### Scenario: Clearing the focus
- **WHEN** the client sends `state: { focus: null }`
- **THEN** the conversation has no account in focus, and the starting snapshot says `focus: null`
