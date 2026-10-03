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

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/me` | — | `{ userId, firmId, role, advisorIds }` |

## Chat

A turn is a **run of the chat agent**: an Agent Framework `AIAgent` behind the official AG-UI server
(`MapAGUIServer`), streamed as [AG-UI](https://github.com/ag-ui-protocol/ag-ui) events over SSE. **Only the
protocol's own events travel** (agui-protocol-only): no `CUSTOM` event, ever. The browser reaches the agent through
CopilotKit's runtime at `/copilotkit/` (below); any AG-UI client may call `/api/chat` directly.

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/conversations` | — | `201 { conversationId }` |
| POST | `/api/chat` | `RunAgentInput` | `text/event-stream` of AG-UI events; `400` without a message (max 4000 chars) or with a malformed `runId`; `404` for another principal's thread or an unknown parent run; `409` for a `runId` used before |
| GET | `/api/runs/{runId}/trace?after=` | — | `{ runId, turnId, ended, events: TraceEvent[] }` — the run's trace while it is written, events after `seq` `after`; the run's owner only, otherwise `404` |

`RunAgentInput` carries `threadId` (the conversation), `runId` (this run — and the id of the turn it records),
`messages` (the last user message is the question; the rest is ignored, the server keeps the conversation), `state`
(the account in focus) and, when answering something a previous run paused for, `resume`. A run without a
`threadId` starts a new conversation; a well-formed `threadId` (`[A-Za-z0-9_-]{1,64}`) that nobody has becomes the
caller's new conversation, since AG-UI clients name their own threads; one issued to another principal is `404`.

```jsonc
{
  "threadId": "c_8f…",
  "runId": "r_2b…",                       // also the turn's id; a new one per run
  "messages": [{ "id": "u_1", "role": "user", "content": "why did run 4417 fail?" }],
  "state": { "focus": { "accountId": "A-1043" } }
}
```

SSE frames are `data: <json>\n\n`; each event names itself in its `type`:

| event | what it carries |
|---|---|
| `RUN_STARTED` | `{ threadId, runId }` — first, exactly once |
| `STATE_SNAPSHOT` | the run's shared state `{ snapshot: { focus: { accountId } \| null } }`: right after `RUN_STARTED`, and again after a read moves the focus — see below |
| `STEP_STARTED` / `STEP_FINISHED` | what the turn is doing: `screening the question`, `tool: <name>`, `checking the answer` — so a client shows progress without the trace |
| `TEXT_MESSAGE_START` / `TEXT_MESSAGE_CONTENT` / `TEXT_MESSAGE_END` | the answer, under one `messageId`. A run that produces no answer opens no message |
| `REASONING_*` | what the model thought on its way to the answer |
| `TOOL_CALL_START` / `TOOL_CALL_ARGS` / `TOOL_CALL_END` / `TOOL_CALL_RESULT` | one tool call, under one `toolCallId`. The start is emitted before the tool runs |
| `ACTIVITY_SNAPSHOT` | a data card: `{ messageId: "card-<toolCallId>", activityType, content }`, right after the carded call's `TOOL_CALL_RESULT` — see below |
| `RUN_FINISHED` | `{ threadId, runId, outcome }` — last. The turn the run recorded is `runId` |
| `RUN_ERROR` | `{ message, code }` — last instead, when the turn failed. Short, generic text only |

**Arguments and results are identifiers and summaries, never free text.** `TOOL_CALL_ARGS.delta` is the
identifier-only arguments as JSON (`{"runId":"4417"}`), not the query a user typed; `TOOL_CALL_RESULT.content` is
structured — `{ tool, summary, sourceCount, sources, isError }` — not the documents the tool found. **Sources travel
here, in the search's own result:** `sources: [{ docId, sectionPath, sourcePath, snippet, kind?, startLine?,
endLine?, symbol?, language? }]`, the snippet the answer shows and nothing more. No event carries the model update it
came from (`rawEvent`). The full result is in the trace.

### The account in focus (shared state)

A conversation has at most one account in focus (add-focus-state):
- **How it is set:** a successful `get_household_portfolio` or `get_aum_history` read sets it; `list_my_accounts` does
  not change it.
- **Client to server:** the client sends `state: { focus: { accountId } | null }` in the `POST /api/chat` body. An id
  is accepted only if a data card in this conversation showed it. `null` clears the focus. Anything else is ignored,
  and the trace records the rejection without the value.
- **What it does:** the model is told the id with "if the question names no account, it is about this one". Data
  routing uses it for an account-less portfolio question. Reads stay firm-scoped by the token whatever it says.

### Data cards

Three read tools' results travel whole, as an AG-UI activity the chat draws as a table (add-activity-cards). Their
result types have no free-text field (a test enforces it). A failed or guard-withheld result sends no card. The
`activityType` is data inside the protocol's own `ACTIVITY_SNAPSHOT`, not an event type of its own.

| `activityType` | tool | `content` |
|---|---|---|
| `maf-lab/holdings` | `get_household_portfolio` | `{ accountId, accountName, householdId, modelPortfolio, driftTolerancePct, outsideTolerance, rebalanceNeeded, holdings: [{ assetClass, marketValue, targetWeightPct, actualWeightPct, driftPct, outsideTolerance, tradeToTarget, tradeSide, weightAfterPct }], totalMarketValue, currency, asOf }` |
| `maf-lab/aum-history` | `get_aum_history` | `{ accountId, householdId, currency, valuations: [{ quarterEnd, aum, changePct }] }` |
| `maf-lab/accounts` | `list_my_accounts` | `{ count, accounts: [{ accountId, name, householdId, modelPortfolio, currency }] }` |

An unknown `activityType` is ignored. A snapshot for a `messageId` already shown replaces that card.

### The trace

A turn's behind-the-scenes trace never travels on the stream. While the run is going, its owner reads it from
`GET /api/runs/{runId}/trace?after=<seq>`, from any replica (the trace is kept in the shared store for the run's grace
period); afterwards, from the stored turn (`GET /api/turns/{turnId}/trace`). The monitor polls the first every 500 ms
while the run is live. See [trace-events.md](trace-events.md).

### A run that waits for a person

When a turn proposes something that needs approval, the run finishes **paused**, on the protocol's own interrupt:

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
  "resume": [{ "interruptId": "adj_9b…", "status": "resolved", "payload": { "approve": true, "idempotencyKey": "adj_9b…:approve" } }]
}
```

That run applies the proposal (or applies nothing, for anything but an approval) and says what happened as its
answer; it records no turn. An interrupt that was already answered, belongs to someone else, or has expired is
refused, and nothing happens twice. A repeat under the same `idempotencyKey` is answered with the first answer; a
different request under it is refused. `metadata.state` is opaque and integrity-protected: hand it back, do not parse
it.

### What a conversation is waiting on

| Method | Path | Response |
|---|---|---|
| GET | `/api/conversations/{id}/pending` | `200 { pending }`; `404` when the conversation is not the caller's own |

`pending` is `null` or
`{ adjustmentId, adjustment, question, expiresAt }`. The run that proposed it is gone once its stream ends; the
proposal is not, so this is how a reopened page finds it again. A proposal that was applied, declined or has
expired is not waiting. A conversation that is not the caller's own is `404`.

The opaque state is deliberately absent: it never leaves the run that issued it, and an answer names the
proposal by `adjustmentId` rather than carrying what would execute.

### Stopping and rejoining

Both go through the protocol; there is no stop or rejoin endpoint beside it.
- **Stop:** the client ends the request (`abortRun`). The run's token is the request's own, so the replica serving
  it stops it within a second, and no tool executes after. Through CopilotKit's runtime, the browser asks the runtime
  to stop the thread (`stopAgent`), and a client that walks away is stopped the same way.
- **Rejoin:** a run on the same thread, naming the lost run as `parentRunId`, with no new message. It replays, as the
  protocol's events, what the lost run had said and done by its last snapshot — the answer, each tool call and how it
  ended — and ends paused on the same question when that run stopped for a person. Another principal's run, and one
  no longer kept, are `404`. (The official server does not serve the protocol's `connect`.)

### Through CopilotKit's runtime

The web reaches every agent through `copilot-runtime`, CopilotKit's runtime, behind the balancer at `/copilotkit/`.
It only wires: each agent is an AG-UI `HttpAgent` to the api, made per request with the caller's bearer token, which
the api checks on every run. It serves exactly:

| Method | Path | Response |
|---|---|---|
| GET | `/copilotkit/info` | the runtime's agents: `chat` and `testgen` |
| POST | `/copilotkit/agent/{chat\|testgen}/run` | the agent's AG-UI run, as above |
| POST | `/copilotkit/agent/{chat\|testgen}/stop/{threadId}` | stops the thread's run; only for the credentials that ran it, otherwise `404` |

Everything else — the runtime's thread listing, thread messages and events, and `connect` — is `404`: the runtime
keeps threads in memory with no owner, so serving them would let one firm read another's conversation.

## Conversation history (owner only)

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/conversations?search=&limit=&before=` | — | `{ conversations: [{ conversationId, title, createdAt, lastActivityAt, turnCount }], nextCursor }` — own, non-deleted, non-empty conversations, newest activity first; `search` matches title, questions and answers (case-insensitive); `limit` default 30, max 100; pass `nextCursor` as `before` for the next page |
| GET | `/api/conversations/{id}` | — | `{ conversationId, title, createdAt, lastActivityAt, turns: [HistoryTurn] }`; `404` when not owned or deleted |
| PATCH | `/api/conversations/{id}` | `{ title }` (1–120 chars) | `204`; `400` invalid title; `404` |
| DELETE | `/api/conversations/{id}` | — | `204` (soft delete: hidden, cannot be continued; turns stay for the review queue); `404` |

`HistoryTurn` = `{ turnId, question, answer, createdAt, toolCalls: [{ callId?, toolName, argumentSummary, outcome,
resultSummary?, sourceCount }], sources: [{ docId, sectionPath, sourcePath, snippet }], feedbackKinds: [string],
traceAvailable, activities: [{ messageId, activityType, content }] }` — `activities` are the turn's data cards, empty
for turns stored before cards existed. The conversation detail also carries `focus: { accountId } | null`. Turns stored before this change may have empty `sourcePath`/`snippet` and null `callId`/`resultSummary`.
The default title is the first question (≤ 80 chars, cut at a word boundary with "…"). A run on a deleted
conversation's id returns `404`.

## Turn traces

| Method | Path | Response |
|---|---|---|
| GET | `/api/turns/{turnId}/trace` | `{ turnId, conversationId, createdAt, events: TraceEvent[], aguiFrames: RunFrame[] \| null }` — the turn's owner, or a FIRM_ADMIN of the same firm for turns in the review queue; otherwise `404`. Kept for `Tracing:RetentionDays` (7). `aguiFrames` is the run's own events as they crossed the wire, and is `null` for a turn answered before they were kept. |

`RunFrame` = `{ seq, atMs, type, bytes, payload?, truncated }` — see [trace-events.md](trace-events.md). Turns recorded
before agui-protocol-only may also carry `name` and `traceSeq` on their custom-event frames.

## Runs

A run's snapshot — `RunState` = `{ runId, conversationId, userId, firmId, answer, toolCalls, outcome, awaitingId, turnId,
error, startedAt, updatedAt }`, `outcome` one of `running`, `answered`, `awaiting_person`, `failed`, `cancelled` — is kept
in the shared store so a rejoin can be answered by any replica (see Stopping and rejoining). It is a snapshot, not a
replay, and is no longer served on its own — see [shared-state.md](shared-state.md).

## Telemetry

| Method | Path | Response |
|---|---|---|
| GET | `/api/telemetry?window=15m\|1h\|6h\|24h` | `TelemetryReport` — any signed-in user. A window not on that list is `400`. |

`TelemetryReport` = `{ window, generatedAt, available, reason, panels: TelemetryPanel[], traceUrl }`.
`TelemetryPanel` = `{ id, title, unit, series: { label, value }[] }`; an empty `series` means nothing was measured
in the window, which is not the same as zero. `available: false` with a `reason` means the metrics store could not
be read.

The queries behind the panels live in the api. A caller chooses the period and nothing else, so this is not a way
to run arbitrary queries against the metrics store, and the store is never reachable from a browser.

Through the load balancer: `/jaeger` opens the trace store, and `/v1/traces` is where the browser's own spans go.

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
| GET | `/api/admin/index/drift` | — | `DriftReport` for the caller's readable tenants: the index against the source, plus `graph` (add-graph-drift) — `{ available, reason, outOfSync, outOfSyncPercent, missingFromGraph, behind, notInCorpus }`, the billing graph against the same source documents; `available: false, reason: "unreachable"` when Neo4j cannot be read |
| POST | `/api/admin/index/run` | — | `202 AdminJob` |
| POST | `/api/admin/index/migrate` | `{ targetModel? }` | `202 AdminJob` |
| GET | `/api/admin/jobs/{jobId}` | — | `AdminJob` |
| GET | `/api/admin/intent-stats?window=1h\|24h\|7d` | — | `IntentStatsReport` for the caller's firm (default `24h`; another window is `400`) |
| GET | `/api/admin/jev-stats?window=1h\|24h\|7d` | — | `JevStatsReport` for the caller's firm, same windows |

`AdminJob.state`: `queued` \| `running` \| `succeeded` \| `failed`. `migrate` accepts `{}` or no body.

`ReviewQueueItem.toolCalls[]`: `{ toolName, argumentSummary, outcome, sourceCount, docIds, chunkIds }` —
`chunkIds` lets a reviewer pick the relevant chunks for a retrieval label.

`ReviewQueueItem.signals`: `negative_feedback`, `rephrased`, `no_tool_on_how_why`,
`zero_retrieval_results`, `long_answer_without_sources`.

`LabelRequest.dataset`: `selection` (uses `expectedTools`), `retrieval` (uses
`relevantChunkIds`), `generation` (uses `referenceAnswer`, `expectedDocIds`).

The two statistics reports are numbers only: no question, answer, passage or identifier of a turn, conversation or
user leaves the server. `IntentStatsReport` = `{ window, from, to, bucketMinutes, settings, totals, pipeline,
reasons, choices, timeline, confidence, inDomain, points, meanProbabilities, latency, models }` — how the intent classifier
answered on the firm's turns. `JevStatsReport` = `{ window, from, to, bucketMinutes, overview, intent, guardrail,
relevance, routing, domains?, answerCheck? }` — every Jev call site on the firm's chat turns, with `intent` equal to
the intent-stats report for the same window. Calls on the A2A path have no turn trace and are not counted. The
shapes are in `Maf.Lab.Domain/Intent` and `Maf.Lab.Domain/Jev`. `answerCheck` = `{ answers, checked, pass,
notRelevant, notGrounded, unchecked, unavailable, relevantFloor, groundedFloor, latency, uncertain? }`: `checked` counts
every verdict but `unchecked`, the two "not" counts are taken against the floor recorded with each check, and
`uncertain` — optional, so an older client still reads the response — counts the checked answers in the review band,
which raise no review signal. An answer left unchecked because its sources were over the cap sent no request.

## Code (any authenticated role)

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/code/snippets` | `{ question, maxResults? }` | `CodeSearchResult`; `400` without a question; `503` when code search cannot answer |

The Code snippets tab: `search_codebase` on the codebase MCP server, called with the caller's own bearer token, so the
server derives the principal itself. `maxResults` defaults to 8 and is clamped to 1–10.
`CodeSearchResult` = `{ results: [{ path, startLine?, endLine?, symbol?, section, kind, language, score, snippet }],
totalMatches, truncated, refineHint? }` — snippets only, never a synthesized answer. A chunk longer than
`CodeSearch:SnippetMaxChars` (1200) comes back as the window of whole lines around the lines that match the query's
terms, with `…` where lines were cut, and `startLine`/`endLine` are the window's; a query with no matching line gets the
chunk's first lines.

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
| GET | `/api/admin/a2a/test-agent` | — | `{ status, card, connection, defaultModel, modelsAllowed, limits, defaultBudget, runs, recent }` |

`inbound` is one row per task a partner started — partner, operation, state, when it started and last changed,
how long it took, and whether it can still be cancelled. `outbound` is one row per consultation this system asked
of the reviewer, with its outcome. `deliveries` is every push-webhook attempt, with its attempts and its error.
No message content appears anywhere: the operation name, the state and the duration are what an operator needs.

Both are scoped by the caller's firm, taken from the principal. An inbound task carries the firm its partner was
entitled to act for, stamped when the task was created; a task belonging to another firm answers `404`, not
`403`, because its existence is not the caller's business. Cancelling goes through the same `CancelTask` a
partner's cancel does, and is itself audited as `a2a.cancel`.

`test-agent` is the test-generation agent as the api knows it; the browser never reaches the agent, which is on the
internal network only. `status` is `{ configured, reachable, reason, latencyMs, checkedAt }`: the api fetches the
agent's public card from `TestAgent:BaseUrl` anonymously, within `TestAgent:ProbeTimeout` (2 s), and reuses the answer
for `TestAgent:ProbeCacheFor` (10 s). Reachable means the card answered, not that a run would succeed; `reason` is a
short sentence, never an exception. `card` (null when it could not be read) is
`{ name, description, version, skills: [{ id, name, description, tags }], endpoint, protocolVersion, requiredScopes,
streaming, pushNotifications }`, as the card states it. `connection` is `{ baseUrl, clientId }` — where the api
reaches the agent and the partner id it signs in as; the secret is never included. `defaultModel` is the allowlist's
default, and `limits` is the same `{ maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt, deadlineMinutes,
maxSuspectedBugs }` of `{ min, max, default }` that `/api/coverage/models` returns; `defaultBudget` has null caps,
because a run started without a budget has none. `runs` is `{ running, candidates, accepted, failed, other, total }`
(running is submitted, working or verifying; failed includes verification failed), and `recent` is the ten runs that
changed last, `[{ id, path, state, reason, attempt, maxAttempts, lastPct, targetPct, model, updatedAt, startedAt,
finishedAt, durationMs, tokens, costUsd, costIsEstimate, budget }]`. `finishedAt` is when the run's work ended — the first time it left submitted, working
and verifying, into a candidate or a final state — so accepting or discarding a candidate later does not move it; it
is null while the run is running. `durationMs` is the work time: `finishedAt − startedAt`, or, while running, the time
from `startedAt` to this answer (the page counts on from there); null when a stopped run's end is not known. Runs
stored before ends were recorded get one at api start, from their first update in a non-running state (or their last
change when they have no updates). `tokens` and `costUsd` are what the run recorded for the agent's model calls
(so far, while it runs): the tokens priced, in USD, at the model's rates as the api sent them to the agent when the run
started — the amount the run's cost cap is checked against, not a recomputation at today's prices; 0 when no model
call was made or the model is priced at zero. The coverage runner's build and test time is not in it.
`costIsEstimate` is true when the run's model is priced at the lab's estimated rates in `TestAgent:Models`
(`PriceIsEstimate`), or is no longer on that allowlist. `budget` is `{ maxTokens, maxCostUsd }` as chosen at start;
null caps are unlimited. Runs describe the repository, not a firm, so nothing here is scoped by tenant and
the route takes no parameter.

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

## Coverage (any authenticated role reads; FIRM_ADMIN changes, otherwise `403`)

The Coverage screen's API. Coverage describes the repository, so nothing here is scoped to a firm. A run's
lifecycle is `submitted → working → verifying → candidate → accepted | discarded`, or it ends `failed`,
`canceled`, `verification_failed` or `completed_no_change`.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/coverage/tree` | — | `{ hasSnapshot, defaultThresholdPct, files: [{ path, pct, threshold, belowThreshold, candidate, run, … }], folders: [{ path, pct, … }] }` (folders line-weighted) |
| GET | `/api/coverage/files?path=&run=` | — | `{ path, commit, measuredAt, summary, source, lines: [{ line, hits, branchesCovered, branchesTotal, status }], run }`; `run=` shows that run's candidate. `404` for a path no snapshot has; `409 source_unavailable { commit }` when the snapshot's commit is not in the repository (refresh coverage) |
| GET | `/api/coverage/files/history?path=` | — | `[{ snapshotId, commit, measuredAt, pct, kind }]`, newest first |
| PUT | `/api/coverage/thresholds?path=` | `{ pct \| null }` | `200` saved; `409 run_required { currentPct, targetPct }` for a raise above coverage; `409 run_active`; `400` out of range. Admin |
| GET | `/api/coverage/models?path=` | — | `{ models: [{ tag, displayName, inputPerMTok, outputPerMTok, bestFor, isDefault, priceIsEstimate, available, unavailableReason }], limits: { maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt, deadlineMinutes, maxSuspectedBugs: { min, max, default } }, estimate: { fixedInputTokens, inputTokensPerRound, typicalRounds, outputTokensPerAttempt } \| null }`. Per attempt the estimate is input = `fixedInputTokens + inputTokensPerRound × min(rounds, typicalRounds)` and output = `outputTokensPerAttempt`; the picker prices it for the model and limits entered. The deadline's maximum and default are the configured `RunDeadline`. There is no default budget: a run without one is limited only by its attempts and the deadline. Admin |
| POST | `/api/coverage/runs` | `{ path, pct, model, budget?, limits? }`; `budget: { maxTokens?, maxCostUsd? }`, a missing or null cap is unlimited; `limits: { maxAttempts?, toolRoundsPerAttempt?, testRunsPerAttempt?, deadlineMinutes?, maxSuspectedBugs? }`, a missing limit takes its default (10, 40, 2, the configured deadline, 3) | `201` run (saves the threshold); `400` a cap that is not positive (field `budget`) or a limit out of bounds (field `limits`); `409 run_active`; `422 model_rejected`; `503 agent_unavailable`, threshold unchanged. Admin |
| GET | `/api/coverage/runs?path=` | — | `[run]`, newest first. A run carries `reason` (for a completed run, the agent's stop: `target`, `attempts` or `budget`; otherwise why it failed) `budget: { maxTokens, maxCostUsd }` (null is unlimited) and `limits: { maxAttempts, toolRoundsPerAttempt, testRunsPerAttempt, deadlineMinutes, maxSuspectedBugs }` (a null `deadlineMinutes` is the configured deadline) |
| GET | `/api/coverage/runs/{id}` | — | `{ run, report, issues: [{ testKey, title, number, url }] }`. A verified run's `report.verification` is `{ scope, tests, pct, reusedFrom? }`: the api's own verification run, with `reusedFrom: { jobId, completedAt }` when the coverage runner answered it with the result it had computed for the identical request (the agent's whole-suite confirmation); absent on runs verified before it was recorded |
| POST | `/api/coverage/runs/agent` | `RunAgentInput` on thread `testgen:<id>[:<viewer>]` | `text/event-stream` in AG-UI, the only way the browser follows a run — a run of the test-generation run agent behind the official AG-UI server, protocol events only: `RUN_STARTED`, `STATE_SNAPSHOT` with the run's state first and on every change — the summary (incl. `phase`) plus its record: `attempts` (each `{attempt, before, after, build, tests, errors, violations, run?, confirmation?}`; `run` is what the attempt's measured run ran and `confirmation` the whole-suite run that confirmed it, each `{scope, files, tests, reason?, reused, pct?}`, both absent on entries recorded before them), `stop` (`{reason, lastAttempt, bestPct, notStarted}` once the agent stopped), `resumes` (the attempts it took the run over again at, after a restart) and `dropped` (the oldest activity was cut) — then every recorded activity entry in order: a phase as `STEP_STARTED`/`STEP_FINISHED`, a tool call as `TOOL_CALL_START`/`ARGS` (`{path}`)/`END`/`RESULT` (`{outcome, summary}`), model text as `TEXT_MESSAGE_*`, reasoning as `REASONING_*`; a stop and a takeover close the open step first. Ends with `RUN_FINISHED` at `candidate` or a final state, or `RUN_ERROR` when failed or canceled, its reason in the state before it. A run that has ended replays and closes; a thread that is not a known run's is `404`. Each page following a run may suffix the thread with `:<viewer>`, so several can follow it at once |
| POST | `/api/coverage/runs/{id}/cancel` | — | `200` run, `409 not_cancellable`. Admin |
| POST | `/api/coverage/runs/{id}/accept` | — | `200 { run, gitHubProblems }` merged into main; `409 merge_conflict \| main_dirty \| branch_missing \| not_candidate`. Admin |
| POST | `/api/coverage/runs/{id}/discard` | — | `200 { run, gitHubProblems }`, branch deleted, issues closed. Admin |
| POST | `/api/coverage/refresh` | — | `202` admin job (one at a time: a second answers with the first). Admin |
| GET | `/api/coverage/refresh` · `/api/coverage/refresh/{jobId}` | — | the current or named refresh job, `204` when there is none. A failed job's `summary` names why: interrupted (the service stopped), the coverage runner could not be reached, neither toolchain produced a report, or the main branch has no commit |
| POST | `/api/coverage/reports` | multipart `commit`, `toolchain`, `report`, `root?`, `dirty?` | `200 { snapshotId, files, dropped }`; `400` for a report that is not Cobertura. Admin |

## Evals (any authenticated role)

| Method | Path | Response |
|---|---|---|
| GET | `/api/evals/reports` | `EvalReportSummary[]`, newest first |
| GET | `/api/evals/reports/{runId}` | `EvalReport` |

## A2A (partner systems, not users)

The assistant is also an [A2A 1.0](https://a2a-protocol.org) agent. A partner is a **system**, not a person: its
token's audience is the A2A endpoint, it carries no user identity, and what it may see comes from the server's
`A2A:Partners` registration — never from the request. To try it by hand, the A2A Inspector runs with the stack on
http://localhost:7172; the MCP endpoints can be tried the same way in the MCP Inspector on http://localhost:7173 (see
README, "Inspecting A2A, MCP and Redis").

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
