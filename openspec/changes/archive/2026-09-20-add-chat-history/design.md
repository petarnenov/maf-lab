# Design

## Context

See proposal.md. Storage today:
- `ConversationRow(Id, UserId, FirmId, CreatedAt)`.
- `TurnRow`: question, answer, `ToolCallsJson` (`ToolCallRecord` without result summary), `SourcesJson` (doc id and
  section only), signals and labels.
- `FeedbackRow`s per turn and `TurnTraceRow` (7-day retention).
- `MessageRow`s for the agent memory.

The web chat keeps turns only in `chatReducer` memory; `/chat` has no conversation in the URL.
`DatabaseInitializer` creates missing tables idempotently but cannot add columns.

## Goals / Non-Goals

**Goals:**
- Full restore of past turns with the same components as live turns (no second rendering path for cards and sources).
- Owner-only access that follows the existing tenant rules.
- Schema evolution that works on the existing volume with two replicas starting at once.

**Non-Goals:**
- Sharing conversations with other users, exporting them, or pinning and folders.
- Hard deletion of data (retention and the review queue still govern it).
- Semantic search over history (substring search is enough here).

## Decisions

### D1. Schema: additive columns
- `ConversationRow` gains `Title` (nullable), `LastActivityAt`, `DeletedAt` (nullable), with an index on
  `(UserId, FirmId, DeletedAt, LastActivityAt)`.
- `DatabaseInitializer` gains a second pass: for each entity table it reads `PRAGMA table_info` and, for each mapped
  property with no column, runs `ALTER TABLE … ADD COLUMN …`. The type comes from the EF model (TEXT, INTEGER, REAL);
  the default is NULL for nullable columns and a typed default otherwise.
  - A concurrent replica may add the column first; the resulting "duplicate column" error is ignored.
  - Existing rows get `LastActivityAt` backfilled from the latest turn, or else from `CreatedAt`, in one idempotent
    `UPDATE … WHERE LastActivityAt IS NULL OR LastActivityAt = '0001-…'`.

### D2. Richer turn persistence
- `SourcesJson` stores full `SourceRef` objects (doc id, section path, source path, snippet). The review queue's
  chunk-id lookup still reads doc id and section, and those fields remain.
- `ToolCallRecord` gains optional `CallId` and `ResultSummary`, both null for old rows; JSON stays backward compatible.
- On each persisted turn, `ChatTurnRunner` also updates the conversation: `LastActivityAt = now`, and `Title` is set
  from the first question when it is null.

### D3. API
- **`GET /api/conversations?search=&limit=&before=`:** the owner's (user + firm) non-deleted conversations with at
  least one turn, ordered by `LastActivityAt DESC, Id DESC`.
  - `before` is an opaque cursor `"{lastActivityTicks}:{id}"`.
  - Search is a SQL `LIKE` (case-insensitive under SQLite's NOCASE collation) over title, and an `EXISTS` over the turn
    question and answer.
  - Each entry returns `{ conversationId, title, createdAt, lastActivityAt, turnCount }` and the response carries
    `nextCursor`.
- **`GET /api/conversations/{id}`:**
  - Returns `{ conversationId, title, createdAt, lastActivityAt, turns: [HistoryTurn] }`.
  - `HistoryTurn = { turnId, question, answer, createdAt, toolCalls: [{ callId?, toolName, argumentSummary, outcome,
    resultSummary?, sourceCount }], sources: [SourceRef], feedbackKinds: [string], traceAvailable }`.
  - `traceAvailable` is true when a `TurnTraces` row exists.
  - Returns 404 when the conversation is not owned or is deleted.
- **`PATCH /api/conversations/{id}` `{ title }`:** 1–120 characters after trimming, otherwise 400; 204 on success.
- **`DELETE /api/conversations/{id}`:** sets `DeletedAt`; 204, and 404 if not owned.
- **`ConversationService.ResolveAsync`** (used by `POST /api/chat`) also requires `DeletedAt IS NULL`, so deleted
  conversations return 404.
- **Default title:** the first question with whitespace collapsed, cut at the last word boundary before 80 characters
  and followed by "…".

### D4. Web
- **Routes:** `chat` and `chat/:conversationId` both render `ChatPage`.
- **`HistorySidebar`:**
  - TanStack Query `['conversations', search]` with a 250 ms debounced search input and a "Load more" button for
    paging;
  - the active item is highlighted;
  - each item has a menu with "Rename" (inline input, Enter or Escape) and "Delete" (a confirm dialog naming the
    title).
- **Opening a conversation:** `useConversation(id)` loads the conversation and dispatches `hydrate` into
  `chatReducer`, which converts `HistoryTurn`s into the same turn shape live turns use:
  - tool cards come out finished, with the result summary, falling back to the outcome;
  - sources and feedback state are restored;
  - each turn carries a `traceAvailable` flag.
- **Sending:**
  - On `/chat/:id` a message uses that conversation id.
  - On `/chat` the first message creates one; on `done` the app navigates (replace) to `/chat/{id}`, and the list is
    invalidated so the conversation appears at the top.
- **Monitor:** selecting a restored turn loads its stored trace (existing `useTurnTrace`) with time travel. When
  `traceAvailable` is false, it shows "Trace expired (kept 7 days)".
- **Layout:** the grid becomes `[history 260px | chat | monitor]`, the history column collapses to a 44px rail, and under
  1024 px the history becomes an overlay drawer with a toggle button.
- **Errors:** a 404 for `/chat/:id` shows "Conversation not found" with a "Start a new conversation" action.

## Risks / Trade-offs

- [SQLite `LIKE` over all answers is linear] → fine at lab scale. At larger scale, add FTS5 (noted as the trigger in
  DECISIONS).
- [Soft delete keeps content] → intentional (review queue, evals). Hard deletion would follow a retention policy, which
  is out of scope.
- [Turns persisted before this change lack snippets, result summaries and call ids] → they render with what they have;
  the fields are optional in the contract.
