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
| POST | `/api/chat/confirm` | `{ conversationId, adjustmentId, approve }` | `200 { status, adjustment?, message }` |

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
| `confirmation_required` | `{ callId, toolName, adjustmentId, adjustment, question, state }` — a write is waiting for the advisor. At most one per turn, and `done` follows it; no further tool runs in that turn |
| `done` | `{ conversationId, turnId, error? }` — always last |

`adjustment` is `{ adjustmentId, accountId, accountName, currentFee, amount, resultingFee, currency, periodStart, periodEnd }`
— identifiers and amounts, never the advisor's reason. `state` is opaque and integrity-protected: hand it back
unchanged, do not parse it, and do not expect to learn anything from it.

### Answering a confirmation

`POST /api/chat/confirm` is how the advisor answers. Approving applies exactly what the proposal said — the
arguments are re-derived from the state, so nothing that has been said since can change what executes. Rejecting
applies nothing and lets the conversation continue.

| outcome | response |
|---|---|
| applied | `200 { status: "applied", adjustment: { adjustmentId, accountId, previousFee, amount, currentFee, currency, appliedAt, alreadyApplied }, message }` |
| already applied | `200 { status: "already_applied", … }` — the fee moved once, however many answers arrive |
| rejected | `200 { status: "declined", adjustment: null, message }` |
| not waiting, or not this person's | `404` |
| the tools are unavailable | `503` with a short problem detail; nothing was changed |

A proposal belongs to the person it was put to, in the conversation it was made in. Anyone else gets `404` — the
same answer as for a proposal that does not exist.

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

## Compliance (FIRM_ADMIN only, otherwise `403`)

| Method | Path | Query | Response |
|---|---|---|---|
| GET | `/api/admin/compliance/verify` | — | `{ intact, checked, unchained, from, to, head, firstBrokenId, reason }` |
| GET | `/api/admin/compliance/actions` | `from`, `to`, `userId`, `kind`, `limit` (≤200, default 50), `before` | `{ actions, nextCursor }` |
| GET | `/api/admin/compliance/export` | `from`, `to`, optional `userId` | `{ manifest, conversations, turns, actions }` |

Every audited action carries `hash` = SHA-256 over the previous action's hash and its own stored fields, so a
changed or removed row breaks the chain; `verify` walks it by row id and names the first row that does not hold.
`unchained` counts rows written before chaining began — they are reported, never rewritten.

`actions` reads the record newest first, paged by row id: pass the previous page's `nextCursor` as `before` for the
older ones, until it is null. Reading is never recorded — browsing the log must not grow it.

The export's firm comes from the token; a `firmId` parameter is ignored, and `userId` only narrows (a data subject
request). Deleted conversations are included, carrying `deletedAt`. The manifest is
`{ firmId, subjectUserId, from, to, generatedAt, by, counts, sha256, auditChainHead }`.

`sha256` covers the three content sections in a **canonical rendering**, not this service's JSON output, so any
recipient can recompute it: the literal section names `conversations`, `turns`, `actions` each on their own line,
followed by one line per row, fields joined with `U+001F` in the order the DTOs declare them, timestamps as UTC
`yyyy-MM-ddTHH:mm:ss.fffZ`, booleans as `true`/`false`, nulls as the empty string, rows terminated by `\n`.

## Evals (any authenticated role)

| Method | Path | Response |
|---|---|---|
| GET | `/api/evals/reports` | `EvalReportSummary[]`, newest first |
| GET | `/api/evals/reports/{runId}` | `EvalReport` |

## A2A (partner systems, not users)

The assistant is also an [A2A 1.0](https://a2a-protocol.org) agent. A partner is a **system**, not a person: its
token's audience is the A2A endpoint, it carries no user identity, and what it may see comes from the server's
`A2A:Partners` registration — never from the request.

| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/.well-known/agent-card.json` | anonymous | the signed public agent card |
| POST | `/a2a/token` | anonymous | `{ accessToken, tokenType, expiresIn, scope }` for `{ clientId, clientSecret, scope? }` |
| POST | `/a2a` | partner | JSON-RPC 2.0 for every A2A method |
| POST/GET/DELETE | `/a2a/message:send`, `/a2a/message:stream`, `/a2a/tasks…` | partner | the same methods over HTTP+JSON |

Both transports carry the specification's own wire format: `message/send`, `tasks/get`,
`tasks/pushNotificationConfig/set`, `agent/getAuthenticatedExtendedCard`, roles `user`/`agent`, states such as
`input-required`, parts with their `kind`, and a result that *is* the task or the message. The preview SDK
underneath speaks a different dialect; `SpecWire` translates both ways and `DECISIONS.md` lists every divergence.

What a partner can do:

- **Ask about a run** — `message/send` with "status of run 4417" answers with a message, from the run's own record.
- **Ask anything else** — answered by the assistant itself, the same agent and the same MCP tools as the chat UI,
  scoped to the partner's firm.
- **Start a billing run** — a task, streamed over `message/stream`, ending in a `billing-run-result` artifact
  (a data part). The run is **simulated**: it walks the real lifecycle over seeded data and bills nobody.
- **Follow, resume, cancel** — `tasks/resubscribe` sends the whole current task first, so a dropped stream misses
  no transition; `tasks/cancel` stops a working task; a task in `input-required` continues when the caller sends
  the missing value under the same task id.
- **Be notified** — `tasks/pushNotificationConfig/set` registers a webhook, which receives one POST per state
  change carrying the task and the caller's own token in `X-A2A-Notification-Token`.

A request about a firm outside the partner's entitlement is rejected with one fixed sentence and no data — not the
firm, not the run, not whether either exists.

**Verifying the card.** The card carries a JWS in `signatures[0]`: `protected` is base64url JSON
(`{"alg":"HS256","typ":"JOSE"}`), and `signature` is base64url HMAC-SHA256 over
`protected + "." + base64url(canonical(card))`, keyed with the issuer's signing key. `canonical` is the served
card with its `signatures` member removed, every object's members sorted lexicographically, and no whitespace —
`json.dumps(card, sort_keys=True, separators=(",", ":"), ensure_ascii=False)` reproduces it, so a verifier never
has to guess this service's property order. `scripts/a2a_probe.py` does exactly that. In the lab the key is
symmetric, so verification needs the same secret; a real deployment would sign asymmetrically and publish the
public half (see `DECISIONS.md`).

## The compliance reviewer (a second agent)

A separate service, behind the same entry point under `/compliance`, with its own identity and its own card. It is
what the assistant consults before a fee adjustment is applied.

| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/compliance/.well-known/agent-card.json` | anonymous | the signed card: one skill, `review_fee_adjustment` |
| POST | `/compliance/a2a/token` | anonymous | a token for **this** agent's audience, scope `a2a.compliance.review` |
| POST | `/compliance/a2a` | partner | JSON-RPC, and the HTTP+JSON paths beside it |

Send the adjustment as a **data part** — `{ adjustmentId, firmId, accountId, amount, reason }` — and the review
comes back as a task: progress while it works, then a `compliance-verdict` artifact carrying
`{ adjustmentId, decision, reason, reviewedAt, simulated }`. About one review in five stops in `input-required`
and asks for the advisor's justification; sending it under the same task id continues that review. Anything that
is not a fee adjustment is answered with one sentence and no verdict.

The review is **simulated**: a threshold (`Review:RefuseAboveAmount`) and a stopwatch
(`Review:MinDurationMs`/`MaxDurationMs`, 20–60 s in the stack), with `Review:AskForJustificationRate` deciding how
often it asks first. It binds nobody.

**How the assistant authenticates to it.** With its own client credentials (`Compliance:ClientId` /
`Compliance:ClientSecret`), exchanged at the reviewer's own token endpoint. A user's token is never forwarded: the
audiences differ, so a token for `/a2a` is refused at `/compliance/a2a` and the other way round. The reviewer is
found by fetching its card from `Compliance:BaseUrl` — the card says where it answers, and nothing else is
hard-coded. A consultation ends as a verdict, a question, a timeout (`Compliance:Deadline`), an unreachable agent
or a failure, and each one is written to the audit record as `a2a.consultation` — agent, adjustment id, task id,
outcome and duration, never the content.
