# Turn trace events

Every chat turn produces an ordered list of `TraceEvent`s: `{ seq, atMs, kind, title, durationMs?, data, truncated }`.
They are streamed live as an AG-UI custom event named `maf-lab/trace`, whose `value` is one TraceEvent (see
[http-api.md](http-api.md)), and stored with the turn:
`GET /api/turns/{turnId}/trace` → `{ turnId, conversationId, createdAt, events: TraceEvent[], aguiFrames }` (owner, or
a FIRM_ADMIN of the same firm for turns in the review queue; otherwise 404). Text fields are capped at 20,000
characters and a trace at 1 MB; `truncated: true` marks a capped event. JSON is camelCase.

| kind | data |
|---|---|
| `turn.start` | `{ conversationId, turnId, principal: { userId, firmId, role }, apiInstance, question, traceId, traceUrl }` — `traceId` is the OpenTelemetry trace this turn's spans are in and `traceUrl` opens it; both are null where no trace store is configured |
| `intent` | `{ intent, forcedRetrieval, forcedTool, choice, probabilities, confidence, inDomain, model, durationMs, reason, routing, forcedTools, domains }` — intent ∈ Procedural, Mixed, Data, ChitChat, Other; `choice` is Jev's raw chosen option and `probabilities` its probability per option; `confidence` and `inDomain` are Jev's calibrated confidence and the probability the question is in the billing domain; `model` is the versioned Jev id (`jev-*`); `reason` explains an answer that was not acted on (`low confidence (0.xx)`, `outside the domain (0.xx)`, `answer is not one of the known intents`) or none at all (`timed out after Ns`, `rejected (NNN)`, `no answer`, `no key`, `classification disabled`, an exception name); null when used. `routing` (null unless tool routing is on) = `{ tools: { tool: probability }, status, statusConfidence, routedTool, arguments, reason }` — Jev's per-tool necessity probabilities, the `run_status` Choice, the read tool the turn was routed to (or null), its arguments, and why a data turn was not routed |
| `domain` | `{ probabilities: { domain: p }, scopeFloor, inScope: [domain], primary, crossing, forcedSearches: [tool], offered: [domain], unavailable: [domain] }` — where Jev put the question among the domains (billing, portfolio; one Noul each, in the intent request): each domain's probability, the domains at or above the scope floor (`Jev:MinDomainScope`, or the most probable alone when only the gate passed), whether two or more are in scope (`crossing`), the searches a forcing intent issued, and the domains whose servers offered tools this turn. Absent when Jev gave no domain answer |
| `boundary` | `{ from, to, tool, callId, server, hop }` — the turn crossed from one domain to another: this tool call's domain differs from the previous call's. Emitted just before that call's `tool.call`; `hop` counts crossings from 1 |
| `guardrail` | `{ check, tool, callId, decision, threshold, top, topQuestion, withheld, items: [{ index, decision, scores: { questionId: probability } \| null, durationMs, reason }], model, requests, durationMs, reason }` — one content-guard screening: `check` ∈ prompt (rides in the intent request), tool_result (one item per `search_documents` excerpt, else the whole result), reviewer (the compliance reviewer's reason or question); `decision` ∈ pass, blocked (prompt refused, no model call), withheld (items removed from what the model reads), unscreened (Jev failed or timed out — `reason` says why; reads fail open, a reviewer's words are withheld). `requests` is how many Jev requests the screening made itself (one per item with text; 1 for a reviewer; null for the prompt, whose questions ride in the intent request) and `durationMs` is the whole screening's wall-clock time, while each item keeps its own request latency; the title adds "· N Jev requests" when N > 1. Never the screened text |
| `history` | `{ budgetTokens, usedTokens, included: [{ role, text, tokens }], excludedCount }` |
| `prompt` | `{ version, systemPrompt, toolMode, domains: [domain], unavailableDomains: [domain], tools: [{ name, description, inputSchema, domain, server }] }` — `domains` are the domains whose MCP servers offered tools this turn; `unavailableDomains` those whose server could not be reached |
| `model.request` | `{ iteration, model, endpoint, toolMode, temperature, think, tools: [name], messages: [Message] }` |
| `model.response` | `{ iteration, model, text, toolCalls: [{ callId, name, arguments }], finishReason, usage: { inputTokens, outputTokens, totalTokens } \| null, latencyMs }` (durationMs = latency) |
| `tool.forced` | `{ callId, tool, domain, server, arguments, reason }` — issued on the model's behalf (Ollama ignores tool_choice) |
| `tool.call` | `{ callId, tool, domain, server, arguments }` — full arguments; `domain` and `server` name the MCP server that owns the tool |
| `tool.result` | `{ callId, tool, domain, server, isError, latencyMs, mcpInstance, result }` — raw MCP CallToolResult (structuredContent, content, isError) without diagnostics or the relevance summary (`_meta` keys `maf-lab/trace` and `maf-lab/relevance` are lifted out) |
| `retrieval` | `{ callId, instance, tenantScope: [tenantId], settings: { mode, fusion, denseVector, limit, prefetchLimit, rerank, reranker, relevanceGate }, query: { text, original, translated, translationMs, translationNote, terms: [{ term, idf }], denseModel, denseDims }, dense: [Candidate], sparse: [Candidate], fused: [Candidate], rerank: [chunkId] \| null, relevance, timings: { embedMs, sparseEncodeMs, qdrantMs, rerankMs, relevanceMs } }` — `settings.reranker` ∈ `jev`, `llm`, null; `relevance` (null when no Jev judge ran) = `{ gate, floor, judged, max, silenced, model (jev-*), durationMs, reason, scores: [{ chunkId, p }] \| null }`: one Jev request judges the fused candidates, the gate silences the search when `max < floor` (`silenced: true`, `scores` present), and an unavailable judge (`reason` set, `scores` null, `max` null) leaves the search ungated. Present only when retrieval diagnostics were requested (`Agent:TraceRetrieval`) |
| `relevance` | `{ callId, gate, reranker, floor, judged, max, silenced, rerankedByJev, model, durationMs, reason }` (durationMs = the judge's latency) — Jev's relevance judgment of one search, recorded for every search that asked it whether or not diagnostics were requested, from the numbers-only summary `search_documents` returns under `_meta["maf-lab/relevance"]` (or, from an MCP server without it, from `retrieval.relevance`). `reranker` is the reranker in use; `rerankedByJev` says Jev's answer actually ordered the results. Title: `Jev relevance: max 0.87 ≥ floor 0.50 — kept`, `… — silenced`, or `Jev relevance unavailable: <reason> — search left ungated`. Never the query, a passage or a chunk id |
| `reasoning.delta` | `{ offset, text }` — what the model thought on its way to the answer, coalesced by the same rules as `answer.delta` and with its own offsets. A model that reasons in several stretches has each recorded where it happened, so reasoning that came before a tool call is recorded before it. Absent for a model that does not reason |
| `answer.delta` | `{ offset, text }` — the streamed answer, coalesced (≤160 chars or 150 ms per chunk, flushed before tool calls and at the end); offsets are contiguous from 0 and the texts concatenate to the full answer |
| `envelope` | `{ callId, tool, text }` — the exact `<tool_data>` string the model received |
| `tool.unknown` | `{ callId, tool }` — the model asked for a tool that does not exist |
| `audit` | `{ callId, tool, arguments, outcome, durationMs }` — the audit row written (identifiers only) |
| `adjustment` | `{ callId, step, adjustmentId, accountId, amount, currentFee, resultingFee, taskId?, outcome? }` — one per step of a write: `proposed`, `reviewed` (with the A2A task id and the reviewer's outcome), `awaiting_confirmation`. Never the advisor's reason or the reviewer's words |
| `answer.check` | `{ verdict, relevant, grounded, relevantFloor, groundedFloor, model, durationMs, reason, sources, sourceChars, requests }` (durationMs = the Jev request's latency) — Jev's check of the final answer: one request, two Nouls over the state `{ user_question, answer, sources }`, where `sources` is every data envelope the model was handed this turn after the content guard (each search excerpt one by one, any other result whole, a withheld item never; capped at `Jev:AnswerCheck:MaxSourceChars`). `relevant` / `grounded` are the probabilities that the answer addresses the question and that every factual claim in it is supported by the sources; `verdict` ∈ pass, not_grounded (grounded below its floor), not_relevant (relevant below its floor), unchecked (`reason`: `check disabled`, `no key`, `timed out after Ns`, `rejected (NNN)`, `incomplete answer`, an exception name). `sources` and `sourceChars` count what was sent; `requests` is 1, or 0 when disabled or keyless. Recorded only for a turn that reached the model and ended with a non-empty answer — never for a refused prompt, a turn waiting for a person's confirmation or a failed turn — after the answer and before `sources`, so the stored trace and the review signals (`answer_not_grounded`, `answer_not_relevant`) carry it; its latency adds to the turn's. Title: `Jev answer check: relevant 0.93 ≥ 0.50, grounded 0.41 < 0.50 — not grounded`, or `Jev answer check unavailable: <reason> — unchecked`. Never the answer or a source's text |
| `sources` | `{ sources: [{ docId, sectionPath }] }` |
| `signals` | `{ signals: [string] }` |
| `memory` | `{ stored: [{ role, tokens }] }` |
| `turn.end` | `{ durationMs, error, answerChars, toolCalls, sourceCount, domainPath: [domain], domainsTouched: [domain], domainsPredicted: [domain], crossings }` — `domainPath` is the domains the turn's calls went to in order (consecutive repeats collapsed), `domainsPredicted` Jev's in-scope domains; the title says "across billing → portfolio" when the turn crossed |

`Message` = `{ role, contents: [ { type: "text", text } | { type: "functionCall", callId, name, arguments } | { type: "functionResult", callId, result } ] }`.
`Candidate` = `{ rank, chunkId, docId, tenantId, sectionPath, score }`.

`query.text` is the text that was embedded and encoded: when the question was written in another language it is the
translation into the corpus language, and `query.original` is what the user asked (`translated` says which).
`translationNote` explains a query searched as written despite needing translation (timeout, failure, unusable answer).

A question in two domains forces both searches together: `… intent → domain → guardrail → prompt → history → tool.forced
(billing) → tool.forced (portfolio) → tool.call (billing) → … → boundary → tool.call (portfolio) → …`.

Typical order for a procedural question: `turn.start → intent → domain → guardrail → prompt → history → tool.forced → tool.call →
tool.result → retrieval → relevance → guardrail → audit → envelope → model.request → reasoning.delta… → answer.delta… → model.response →
memory → answer.check → sources → signals → turn.end`.
The trace is stored before `done` is sent, so the stored copy is readable as soon as the stream ends. The answer check
runs before both, so the run's terminal event waits for it (at most `Jev:AnswerCheck:TimeoutSeconds`).

## AG-UI frames

The trace says what the system did; the frames say what it put on the wire. Every event of a run is recorded as it
goes out — including the ones a client may ignore (`RUN_STARTED`, `TEXT_MESSAGE_START`/`END`, `TOOL_CALL_END`, a
custom event under an unknown name) and the terminal event — and stored with the turn the run recorded:

`RunFrame` = `{ seq, atMs, type, name?, bytes, traceSeq?, payload?, truncated }`

| field | meaning |
|---|---|
| `seq` | position in the run, from 1 |
| `atMs` | milliseconds since the run's first frame |
| `type` | the protocol event type, e.g. `TEXT_MESSAGE_CONTENT` |
| `name` | a custom event's name, e.g. `maf-lab/trace` |
| `bytes` | the frame's size on the wire |
| `traceSeq` | for a `maf-lab/trace` frame, the `seq` of the TraceEvent it carried |
| `payload` | the event itself; absent for a trace frame and for a frame past the cap |
| `truncated` | true when the run's 256 KB cap left the frame without its payload |

A trace frame is kept by reference: its TraceEvent is already in `events`, and a second copy would double what the
turn holds. The frames ride on the turn's trace row, so they are read by whoever may read the trace and deleted when
it is. A run that records no turn — an answer to a confirmation, or one the client walked away from — has nowhere to
put them, and `aguiFrames` is `null` for a turn whose frames were never recorded.

The web client records the same frames itself as it reads the stream, so a turn on screen shows what actually
arrived (a frame whose JSON did not parse included, as `unparsed`) and a reopened turn shows the stored copy. Both
are listed, one row each, in the monitor's **AG-UI** view.
