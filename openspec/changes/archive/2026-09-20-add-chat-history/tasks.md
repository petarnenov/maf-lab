# Tasks

## 1. Storage

- [x] 1.1 Add `Title`, `LastActivityAt`, `DeletedAt` (+ index) to `ConversationRow`; extend `DatabaseInitializer` with the additive-column pass (PRAGMA table_info → ALTER TABLE ADD COLUMN, duplicate-column tolerant) and the `LastActivityAt` backfill; verify tests: old database gains columns, concurrent initializers succeed, backfill uses the latest turn
- [x] 1.2 Persist full `SourceRef`s in `TurnRow.SourcesJson`, add optional `CallId`/`ResultSummary` to `ToolCallRecord`, and update the conversation's `LastActivityAt` and default `Title` on each turn; verify tests (default title truncation at a word boundary; review-queue chunk lookup still works; old rows deserialize)

## 2. API

- [x] 2.1 `GET /api/conversations` (owner-only, non-empty, non-deleted, ordered by last activity, search over title/questions/answers, limit/cursor paging); verify tests for isolation across users and firms, search, empty-conversation hiding, paging
- [x] 2.2 `GET /api/conversations/{id}` returning `HistoryTurn`s (tool calls, sources, feedback kinds, traceAvailable); verify tests for restore content, older-row fallback, and 404 for other users and deleted conversations
- [x] 2.3 `PATCH` (rename, 1–120 chars) and `DELETE` (soft) endpoints; chat rejects deleted conversations; verify tests incl. review queue still listing a deleted conversation's flagged turn

## 3. Web

- [x] 3.1 Routes `/chat` and `/chat/:conversationId`; `chatReducer` `hydrate` from `HistoryTurn`s; navigate to `/chat/{id}` after the first turn of a new conversation; verify reducer and routing tests (reload restores, 404 view)
- [x] 3.2 `HistorySidebar` (debounced search, list with active highlight, load more, new conversation, rename inline, delete with confirm dialog; collapsible rail and < 1024 px drawer); verify rendering and interaction tests
- [x] 3.3 Restored turns open their stored trace in the monitor with time travel, or "Trace expired"; list refresh after new turns, rename and delete; verify tests

## 4. Verification and docs

- [x] 4.1 In the running stack on :7171: chat, reload on `/chat/{id}`, continue with a follow-up (history window includes earlier turns in the trace), search, rename, delete; switch persona to confirm isolation. Run `make test`, `make lint`, `make ci-e2e`
- [x] 4.2 Update `docs/http-api.md`, README (history section) and DECISIONS.md (schema evolution, soft delete, persistence changes, FTS trigger); verify sections exist
