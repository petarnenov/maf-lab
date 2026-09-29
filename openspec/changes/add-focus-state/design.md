# Design

## Context

- **Input.** `POST /api/chat` takes AG-UI `RunAgentInput`. The endpoint reads the last user message and `Resume`, and
  ignores `State`. The web client sends `{ threadId, runId, messages }`.
- **Routing.** `JevIntentClassifier.ClassifyAsync(question, ct)` asks Jev once per turn. `WithRoute` turns the routing
  answer into a `ToolRoute` through `DataToolRouter`, whose portfolio branch takes the account id from the question by
  regex and gives up with "needs one account id, the question has 0". That fallback is code; the Jev request does not
  involve it.
- **Cards and storage.**
  - Cards (§49) are stored per turn in `TurnRow.ActivitiesJson`, whose content has `accountId`s: holdings and AUM at
    the top level, accounts in `accounts[]`.
  - `ConversationRow` holds per-conversation data, and `DatabaseInitializer` adds new columns additively.
- **Protocol types.** `AGUI.Abstractions` 1.0.0 has `StateSnapshotEvent` and `RunAgentInput.State`, and `@ag-ui/core`
  has `EventType.STATE_SNAPSHOT`.

## Goals / Non-Goals

**Goals:**
- One account in focus per conversation, visible, changeable from the UI.
- Used for account-less follow-ups by both the model and routing.
- Unable to widen access.

**Non-Goals:**
- No other shared state (domain, period, filters) and no `STATE_DELTA`: the state is one field, and a snapshot is
  simpler and cheaper.
- No change to any Jev question, state or threshold.
- Focus does not carry across conversations.

## Decisions

1. **The server owns the focus.**
   - It lives in `ConversationRow.FocusAccountId` (nullable).
   - It resolves at turn start: a valid client-sent value if present, else the stored value.
   - It updates during the turn after a successful single-account read, and is persisted at turn end (and at start
     when the client changed it).
2. **What the client may send.**
   - A focus id is accepted only if it matches `^[A-Z]-\d{2,}$` and is in the conversation's offered set: every
     `accountId` in the `ActivitiesJson` of its stored turns.
   - This limits a crafted request to accounts the user was already shown under their own principal. Even an accepted
     id is only a hint; the portfolio read is firm-scoped by the token.
   - A rejected value is traced as `{ source: "client", accepted: false }` without the value.
   - *Alternative:* validate with a live `list_my_accounts` call. Rejected: an extra MCP round trip on every turn, for
     the same guarantee the principal already gives.
3. **The model note** is a fixed sentence with the id, appended to `Instructions` as its own section:
   `## Conversation focus\nIf the question names no account, it is about account {id}.`
   - Nothing but the validated id is interpolated.
   - *Alternative:* a synthetic user message. Rejected, because it would be stored in history as if the user said it.
4. **Routing fallback.**
   - `IIntentClassifier.ClassifyAsync(question, ct, focusAccountId = null)` passes the focus to
     `DataToolRouter.Route`.
   - In the portfolio branch (not `list_my_accounts`), zero named ids plus a focus routes with the focus.
   - One named id always wins. Two or more still give up.
   - The Jev request is byte-for-byte unchanged. `docs/rules/jev-usage.md` §0.5 applies: the choice of account is
     code's, not Jev's.
5. **Events:**
   - The runner writes `StateSnapshotEvent { Snapshot = { focus } }` right after `RUN_STARTED`.
   - When a read moves the focus, it writes another after that call's card.
   - The trace gets a `focus` kind: `{ accountId?, source: stored|client|none|read, accepted?, previous? }`.
6. **Web:**
   - `chatEvents` maps `EventType.STATE_SNAPSHOT` into `{ type: 'state', data: { focus } }`, and the reducer keeps
     `state.focus`.
   - `hydrate` reads `ConversationDetail.focus`.
   - `useChatStream.send` adds `state: { focus }` to every request.
   - `FocusChip` above the composer; "Focus" / "Фокус" buttons on cards call `setFocus(id)`, a reducer action that
     starts no run.
7. **History API:** `ConversationDetail` gains `Focus` (a nullable `{ accountId }`), so older clients ignore it.

## Risks / Trade-offs

- [A stale focus answers the wrong account: the user means another account without naming it] → The chip shows the
  focus before sending, ✕ clears it, and a named id always wins.
- [The model ignores the note] → Routing covers data turns deterministically. Measured live on a follow-up.
- [The offered-set query on every turn] → A single indexed read of the conversation's turns' `ActivitiesJson`: small
  JSON, one conversation.
