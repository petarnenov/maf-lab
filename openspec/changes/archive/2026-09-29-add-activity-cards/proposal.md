# Proposal

## Why

Portfolio data reaches the user only as whatever the model writes about it, and today that is raw markdown the chat
does not render: pipes, `|---|` rows and `**` around numbers. The server has the exact data, a typed DTO from the tool
call, but redacts it to a one-line summary before it reaches the client (DECISIONS §26). The chat also handles only
10 of AG-UI's 30 event types.

The protocol has a message kind made for this, the **activity**: `ACTIVITY_SNAPSHOT` carries an `activityType` and
structured `content`, and the client renders it as a component. It arrives when the tool returns, before the model has
written a word.

## What Changes

- **A card per portfolio read.** When `get_household_portfolio`, `get_aum_history` or `list_my_accounts` returns
  successfully, the run emits one `ACTIVITY_SNAPSHOT`:
  - `activityType` is `maf-lab/holdings`, `maf-lab/aum-history` or `maf-lab/accounts`;
  - `content` is the tool's typed result as the server built it: numbers, enums, dates, ids and the firm's own account
    and household names. It has no free-text field.
  - It goes out right after the tool's `TOOL_CALL_RESULT`.
- **The holdings card includes the rebalance plan** (from `add-rebalance-plan`):
  - trade per class, marked buy or sell by word and icon, not by colour alone;
  - the weight after;
  - a drift bar with the tolerance band;
  - a "rebalance needed / not needed" badge.
- **Presentation of all cards:**
  - numbers right-aligned in tabular figures and formatted for the question's language (bg-BG or en-US) with the
    result's currency;
  - a caption naming the account and its as-of date, and a total row;
  - "Copy as CSV";
  - horizontal scroll inside the card on a phone, and the app's light and dark tokens.
- **Cards are kept with the turn.** They are stored with the turn and restored with the conversation, and they appear
  at their step when a turn is replayed (time travel). A trace event records each card, with its type and numbers
  only.
- **The redaction contract is amended, not bypassed.** `agui-stream` states that tool results travel as identifiers
  and summaries, and that a card may carry a result only for a tool whose declared result type has no free-text field.
  Every other result is redacted as today. A withheld (guardrail) or failed result emits no card.
- An unknown `activityType` is ignored by the client, as the protocol requires.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `agui-stream`: tool results may travel as an activity card for tools on a free-text-free allow-list; a new
  requirement for the activity event.
- `web-ui`: the chat renders data cards for the three activity types.
- `chat-history`: a restored conversation shows its turns' cards.

## Impact

- **api:**
  - `Agent/ChatTurnRunner.cs`: the tool middleware emits the snapshot after screening.
  - `Agent/Streaming/AGUIStream.cs`: the activity factory and the allow-list.
  - `Storage/MafDbContext.cs`: `TurnRow.ActivitiesJson`, an additive column.
  - `ConversationService`: `HistoryTurn.Activities`.
  - A trace kind `card`.
- **web:**
  - `chatEvents.ts`: `EventType.ACTIVITY_SNAPSHOT`.
  - `chatReducer.ts`: `AssistantTurn.cards`.
  - New `web/src/chat/cards/`: `HoldingsCard`, `AumHistoryCard`, `AccountsCard` and `formatMoney`.
  - `ChatPage.tsx`, and the time-travel reconstruction.
- **Depends on** `add-rebalance-plan`, for the holdings card's plan columns.
- **Dependencies:** none added; `AGUI.Abstractions` 1.0.0 already has `ActivitySnapshotEvent`, and `@ag-ui/core` 1.0.0
  has `EventType.ACTIVITY_SNAPSHOT`.
- The A2A path and the eval host are unchanged: they do not render AG-UI.
