# Proposal

## Why

A portfolio conversation is usually about one account: "rebalance A-1043", then "and its AUM?", then "what model
does it follow?". Today the follow-ups work only if the model carries the account over from history on its own. Data
routing, which saves a model call, gives up on them outright: "needs one account id, the question has 0". Nothing on
screen says which account the conversation is about, and the user cannot change it without typing an id.

AG-UI has a slot for exactly this: shared state (`STATE_SNAPSHOT`), which the agent sends and the client sends back
with each run (`RunAgentInput.state`).

## What Changes

- **The conversation has an account in focus.**
  - It is set by the server when a portfolio read for exactly one account succeeds: `get_household_portfolio` or
    `get_aum_history`.
  - It is kept with the conversation and restored when the conversation is reopened.
  - `list_my_accounts` does not change it.
- **It travels as AG-UI shared state.**
  - Every run emits a `STATE_SNAPSHOT` `{ focus: { accountId } | null }` after `RUN_STARTED`, and again when a read
    changes it.
  - The client sends its current state back in `RunAgentInput.state` with each run.
- **The user can change it from the UI.**
  - A chip above the message box shows the focus. ✕ clears it.
  - Each row of an accounts card, and each holdings or AUM card, has "Focus". Only accounts the server has already
    shown in this conversation's cards can be chosen.
  - Any other id sent in `state` is ignored, and the trace says so. The client can never widen what the user may see.
    Every portfolio read is still firm-scoped by the principal.
- **Follow-ups resolve to it.**
  - The model is told which account is in focus, as a single validated id in a system note: "If the question names no
    account, it is about <id>".
  - Data routing uses the focus when a portfolio question names no account, instead of giving up.
  - No Jev question changes. The fallback is code (the Jev rule: never ask Jev what code can compute).
- The trace records the focus the turn started with, where it came from (server, client or none), and any change or
  rejection.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `agui-stream`: the run's shared state (`STATE_SNAPSHOT` out, `RunAgentInput.state` in) carries the account in focus.
- `chat-agent`: follow-ups and data routing resolve an account-less portfolio question to the account in focus.
- `web-ui`: the focus chip, and "Focus" on cards.
- `chat-history`: the focus is kept with the conversation.

## Impact

- **api:**
  - `ConversationRow.FocusAccountId` (additive column).
  - `ChatEndpoints`: pass `input.State` to the runner.
  - `ChatTurnRunner`: resolve and validate the focus, the system note, `STATE_SNAPSHOT`, and the update after a read.
  - `DataToolRouter`: the focus fallback.
  - `HistoryEndpoints`: `ConversationDetail.focus`.
  - A `focus` trace kind.
- **web:**
  - `useChatStream`: send `state`, read `STATE_SNAPSHOT`.
  - `chatReducer`: `focus`.
  - A `FocusChip` in `ChatPage`.
  - "Focus" buttons in `cards/`.
- **Depends on** `add-activity-cards` (archived): the cards are what the allowed ids are read from.
- The A2A path and evals keep working without state. Selection may improve on follow-ups; it is measured, not
  assumed.
- No packages.
