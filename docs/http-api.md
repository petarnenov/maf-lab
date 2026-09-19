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

## Evals (any authenticated role)

| Method | Path | Response |
|---|---|---|
| GET | `/api/evals/reports` | `EvalReportSummary[]`, newest first |
| GET | `/api/evals/reports/{runId}` | `EvalReport` |
