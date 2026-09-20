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

A turn is a **run of the agent**, streamed as [AG-UI](https://github.com/ag-ui-protocol/ag-ui) events over SSE.

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/conversations` | — | `201 { conversationId }` |
| POST | `/api/chat` | `RunAgentInput` | `text/event-stream` of AG-UI events |
| POST | `/api/chat/{runId}/stop` | — | `202` when this instance was running it, `404` otherwise |

`RunAgentInput` carries `threadId` (the conversation), `runId` (this turn), `messages` (the last user message is
the question) and, when answering something a previous run paused for, `resume`. A run without a `threadId`
starts a new conversation, whose id arrives on the run's terminal event. A `threadId` issued to another
principal returns `404`.

```jsonc
{
  "threadId": "c_8f…",                 // omit to start a new conversation
  "runId": "r_2b…",
  "messages": [{ "id": "u_1", "role": "user", "content": "why did run 4417 fail?" }]
}
```

SSE frames are `event: <TYPE>\ndata: <json>\n\n`, where `<TYPE>` is the event's own protocol discriminator:

| event | what it carries |
|---|---|
| `RUN_STARTED` | `{ threadId, runId }` — first, exactly once |
| `TEXT_MESSAGE_START` / `TEXT_MESSAGE_CONTENT` / `TEXT_MESSAGE_END` | the answer, under one `messageId`. A run that produces no answer opens no message |
| `TOOL_CALL_START` / `TOOL_CALL_ARGS` / `TOOL_CALL_END` / `TOOL_CALL_RESULT` | one tool call, under one `toolCallId`. The start is emitted before the tool runs |
| `CUSTOM` | this system's own events, by `name` — see below |
| `RUN_FINISHED` | `{ threadId, runId, outcome, result }` — last. `result.turnId` is the turn, which feedback names |
| `RUN_ERROR` | `{ message }` — last instead, when the turn failed. Short user-facing text only |

**Arguments and results are identifiers and summaries, never free text.** `TOOL_CALL_ARGS.delta` carries the
argument summary (`runId=4417`), not the query a user typed; `TOOL_CALL_RESULT.content` is structured —
`{ tool, summary, sourceCount, isError }` — not the documents the tool found. The full result is in the trace.

### The two names this system adds

| custom `name` | value |
|---|---|
| `maf-lab/sources` | `{ sources: [{ docId, sectionPath, sourcePath, snippet }] }` — before the run ends |
| `maf-lab/trace` | one turn-trace event `{ seq, atMs, kind, title, durationMs?, data, truncated }`; see [trace-events.md](trace-events.md) |

A consumer that does not recognise a custom event ignores it and still follows the run.

### A run that waits for a person

When a turn proposes something that needs approval, the run finishes **paused**:

```jsonc
{
  "type": "RUN_FINISHED",
  "outcome": {
    "type": "interrupt",
    "interrupts": [{
      "id": "adj_9b…",                 // what an answer names
      "message": "Apply a fee adjustment of -200.00 USD to A-1042 (…)?",
      "reason": "approval_required",
      "toolCallId": "c1",
      "expiresAt": "2026-09-20T15:21:14Z",
      "responseSchema": { "type": "object", "properties": { "approve": { "type": "boolean" } } },
      "metadata": { "adjustment": { "accountId": "A-1042", "currentFee": 1200, "…": "…" }, "state": "…", "tool": "propose_fee_adjustment" }
    }]
  }
}
```

Nothing has been changed and no further tool runs in that run. Answering is a **new run** that resumes it:

```jsonc
{
  "threadId": "c_8f…",
  "runId": "r_3c…",
  "messages": [],
  "resume": [{ "interruptId": "adj_9b…", "payload": { "approve": true } }]
}
```

That run applies the proposal (or applies nothing, for anything but an approval) and says what happened as its
answer. An interrupt that was already answered, belongs to someone else, or has expired is refused, and nothing
happens twice. `metadata.state` is opaque and integrity-protected: hand it back, do not parse it.

### What a conversation is waiting on

`GET /api/conversations/{id}/pending` → `200 { pending }`, where `pending` is `null` or
`{ adjustmentId, adjustment, question, expiresAt }`. The run that proposed it is gone once its stream ends; the
proposal is not, so this is how a reopened page finds it again. A proposal that was applied, declined or has
expired is not waiting. A conversation that is not the caller's own is `404`.

The opaque state is deliberately absent: it never leaves the run that issued it, and an answer names the
proposal by `adjustmentId` rather than carrying what would execute.

### Stopping

`POST /api/chat/{runId}/stop` ends a run within a second, and no tool executes after it. Abandoning the stream
does the same thing through the request itself and is what a browser actually does. The registry of running runs
is per instance, so a stop sent to the replica that is not running the turn answers `404`.

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

`kind`: `wrong_tool` \| `wrong_document` \| `wrong_answer` \| `wrong_confirmation`. The last one is about the
summary a person was asked to approve, so the UI offers it only on a turn that asked for one.

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

## Agent to agent (FIRM_ADMIN only, otherwise `403`)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/admin/a2a` | — | `{ inbound, outbound, deliveries }` |
| POST | `/api/admin/a2a/tasks/{id}/cancel` | — | `{ taskId, state }`, `404` unknown, `409` already finished |

`inbound` is one row per task a partner started — partner, operation, state, when it started and last changed,
how long it took, and whether it can still be cancelled. `outbound` is one row per consultation this system asked
of the reviewer, with its outcome. `deliveries` is every push-webhook attempt, with its attempts and its error.
No message content appears anywhere: the operation name, the state and the duration are what an operator needs.

Both are scoped by the caller's firm, taken from the principal. An inbound task carries the firm its partner was
entitled to act for, stamped when the task was created; a task belonging to another firm answers `404`, not
`403`, because its existence is not the caller's business. Cancelling goes through the same `CancelTask` a
partner's cancel does, and is itself audited as `a2a.cancel`.

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
