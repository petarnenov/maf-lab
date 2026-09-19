# Proposal

## Why

Conversations, turns, feedback and traces are already stored per user, but the UI forgets them. After a reload the chat
is empty, and there is no way back to yesterday's investigation or to continue it. Chat history makes the lab usable
across sessions and gives the monitor's stored traces and time travel a natural entry point.

## What Changes

- A **history sidebar** on `/chat` lists the user's own conversations, newest activity first. Each entry has a title
  (the first question by default), last activity time and turn count. The list can be searched over titles, questions
  and answers.
- **Open a conversation** to restore all of its turns: questions, answers, tool cards, sources, feedback already given,
  and the behind-the-scenes monitor with time travel for each turn. Traces older than the 7-day retention show as
  expired.
- **Continue** an opened conversation. The agent's memory already follows the conversation id.
- **Manage:** rename a conversation, and delete it after a confirmation. Deletion hides the conversation from the user
  and blocks continuing it; its turns stay in the database for the review queue and eval labels.
- The active conversation is part of the URL (`/chat/:conversationId`), so a reload or a shared link reopens it for the
  same user; for anyone else it is not found.
- **Richer turn persistence:** a turn now stores its full source references (doc id, section, source path, snippet) and
  each tool call's result summary and call id, so restored turns render like live ones. Older turns fall back
  gracefully.
- **Additive schema evolution:** the database initializer also adds new columns to existing tables. The api-data
  volume already holds a database, and two api replicas start concurrently.

## Capabilities

### New Capabilities
- `chat-history`: listing, searching, opening, continuing, renaming and deleting one's own conversations; restoring a
  turn's full view; URL addressing; ownership rules.

### Modified Capabilities
<!-- None: live chat, SSE and memory behaviour are unchanged; restored turns reuse existing views. -->

## Impact

- API:
  - endpoints: `GET /api/conversations` (search, paging), `GET /api/conversations/{id}`,
    `PATCH /api/conversations/{id}` (title) and `DELETE /api/conversations/{id}` (soft delete);
  - the chat endpoint rejects deleted conversations;
  - `ConversationRow` gains Title, LastActivityAt and DeletedAt;
  - `TurnRow.SourcesJson` stores full `SourceRef`s, and `ToolCallRecord` gains optional `CallId` and `ResultSummary`;
  - `DatabaseInitializer` adds missing columns.
- Web: a `HistorySidebar` (a collapsible column, a drawer on narrow screens), the `/chat/:conversationId` route,
  a conversation loader that hydrates `chatReducer`, and rename/delete dialogs. Tests.
- Docs: `docs/http-api.md`, README, DECISIONS.
