# maf-lab HTTP API (Maf.Lab.Api)

Base URL: `http://localhost:7171` (the load balancer; the MCP endpoint is `/mcp` on the same origin).
Without the balancer (local `dotnet run`): `http://localhost:5080`. Every response carries `X-Instance`, the replica
that served it. JSON is camelCase. DTOs live in `src/Maf.Lab.Domain`.
Every `/api/*` route requires `Authorization: Bearer <jwt>` from the dev issuer.
Tenant is taken from the token only — no route or body has a tenant field.

## Dev issuer (development only)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/dev/users` | — | `[{ userId, firmId, role, advisorIds, label }]` predefined personas |
| POST | `/dev/token` | `{ userId, firmId, role, advisorIds? }` | `{ token, expiresAt }` |

Roles: `FIRM_ADMIN`, `ADVISOR`, `OPS`, `READ_ONLY`. Firms: `firm-a`, `firm-b`, `firm-c`.

## Identity

| GET | `/api/me` | — | `{ userId, firmId, role, advisorIds }` |
|---|---|---|---|

## Chat

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/conversations` | — | `201 { conversationId }` |
| POST | `/api/chat` | `{ conversationId?, message }` | `text/event-stream` |

If `conversationId` is omitted a new conversation is created; its id arrives in `done`.
A conversation id issued to another principal returns `404`.

SSE frames are `event: <name>\ndata: <json>\n\n`:

| event | data |
|---|---|
| `tool_call_started` | `{ callId, toolName, argumentSummary }` — emitted before the tool executes. `argumentSummary` is `key=value` pairs of identifiers only (e.g. `runId=4417`, `sourceTypes=docs`), never the free-text query |
| `tool_call_finished` | `{ callId, toolName, resultSummary, sourceCount, isError }` |
| `sources` | `{ sources: [{ docId, sectionPath, sourcePath, snippet }] }` — before `done` |
| `text_delta` | `{ text }` |
| `trace` | one turn-trace event `{ seq, atMs, kind, title, durationMs?, data, truncated }` — interleaves with everything, all before `done`; see [trace-events.md](trace-events.md) |
| `done` | `{ conversationId, turnId, error? }` — always last |

Errors during a streamed turn arrive as `done.error` (short user-facing text); there is no separate `error` event.

## Conversation history (owner only)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/conversations?search=&limit=&before=` | — | `{ conversations: [{ conversationId, title, createdAt, lastActivityAt, turnCount }], nextCursor }` — own, non-deleted, non-empty conversations, newest activity first; `search` matches title, questions and answers (case-insensitive); `limit` default 30, max 100; pass `nextCursor` as `before` for the next page |
| GET | `/api/conversations/{id}` | — | `{ conversationId, title, createdAt, lastActivityAt, turns: [HistoryTurn] }`; `404` when not owned or deleted |
| PATCH | `/api/conversations/{id}` | `{ title }` (1–120 chars) | `204`; `400` invalid title; `404` |
| DELETE | `/api/conversations/{id}` | — | `204` (soft delete: hidden, cannot be continued; turns stay for the review queue); `404` |

`HistoryTurn` = `{ turnId, question, answer, createdAt, toolCalls: [{ callId?, toolName, argumentSummary, outcome,
resultSummary?, sourceCount }], sources: [{ docId, sectionPath, sourcePath, snippet }], feedbackKinds: [string],
traceAvailable }`. Turns stored before this change may have empty `sourcePath`/`snippet` and null `callId`/`resultSummary`.
The default title is the first question (≤ 80 chars, cut at a word boundary with "…"). `POST /api/chat` with a deleted
conversation id returns `404`.

## Turn traces

| Method | Path | Response |
|---|---|---|
| GET | `/api/turns/{turnId}/trace` | `{ turnId, conversationId, createdAt, events: TraceEvent[] }` — the turn's owner, or a FIRM_ADMIN of the same firm for turns in the review queue; otherwise `404`. Kept for `Tracing:RetentionDays` (7). |

## Feedback

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/feedback` | `{ conversationId, turnId, kind, comment? }` | `202 { feedbackId }` |

`kind`: `wrong_tool` \| `wrong_document` \| `wrong_answer`.

## Admin (FIRM_ADMIN only, otherwise `403`)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/admin/feedback/queue` | — | `ReviewQueueItem[]` |
| POST | `/api/admin/feedback/{turnId}/label` | `LabelRequest` | `204` |
| GET | `/api/admin/index/status` | — | `IndexStatus` |
| GET | `/api/admin/index/drift` | — | `DriftReport` |
| POST | `/api/admin/index/run` | — | `202 AdminJob` |
| POST | `/api/admin/index/migrate` | `{ targetModel? }` | `202 AdminJob` |
| GET | `/api/admin/jobs/{jobId}` | — | `AdminJob` |

`AdminJob.state`: `queued` \| `running` \| `succeeded` \| `failed`. `migrate` accepts `{}` or no body.

`ReviewQueueItem.toolCalls[]`: `{ toolName, argumentSummary, outcome, sourceCount, docIds, chunkIds }` —
`chunkIds` lets a reviewer pick the relevant chunks for a retrieval label.

`ReviewQueueItem.signals`: `negative_feedback`, `rephrased`, `no_tool_on_how_why`,
`zero_retrieval_results`, `long_answer_without_sources`.

`LabelRequest.dataset`: `selection` (uses `expectedTools`), `retrieval` (uses
`relevantChunkIds`), `generation` (uses `referenceAnswer`, `expectedDocIds`).

## Topology (any authenticated role)

| Method | Path | Body / query | Response |
|---|---|---|---|
| GET | `/api/topology` | — | `TopologyReport`: `{ generatedAt, cacheSeconds, discoveryAvailable, reportedBy, nodes, edges }` |
| GET | `/api/topology/diagram` | — | The drawn diagram (`docs/topology.drawio`) as `application/xml` |

A node is `{ id, name, health, instances, facts, reason, latencyMs }`; `health` is `Healthy`, `Degraded`,
`Unreachable` or `NotProbed` (sent by name). `instances` are the replicas found by resolving the compose service
name, each asked its own `/health`; `facts` are display strings (chunk count, models, tool list) and never carry a
secret — the chat provider reports only *whether* its key is configured. The report is probed concurrently with a
2 s budget per service and reused for `cacheSeconds`.

## Evals (any authenticated role)

| Method | Path | Response |
|---|---|---|
| GET | `/api/evals/reports` | `EvalReportSummary[]`, newest first |
| GET | `/api/evals/reports/{runId}` | `EvalReport` |
