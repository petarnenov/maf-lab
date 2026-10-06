The caller's own conversations, listed, renamed and deleted, over the core's conversation store (owner only).
Reopening one by its id is the core's (`GET /api/conversations/{id}`).

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/conversations?search=&limit=&before=` | — | `{ conversations: [{ conversationId, title, createdAt, lastActivityAt, turnCount }], nextCursor }` — own, non-deleted, non-empty conversations, newest activity first; `search` matches title, questions and answers (case-insensitive); `limit` default 30, max 100; pass `nextCursor` as `before` for the next page |
| PATCH | `/api/conversations/{id}` | `{ title }` (1–120 chars) | `204`; `400` invalid title; `404` |
| DELETE | `/api/conversations/{id}` | — | `204` (soft delete: hidden, cannot be continued; turns stay for the review queue; recorded as `conversation.delete` in the audit); `404` |
