# Turn trace events

Every chat turn produces an ordered list of `TraceEvent`s: `{ seq, atMs, kind, title, durationMs?, data, truncated }`.
They are streamed live as an AG-UI custom event named `maf-lab/trace`, whose `value` is one TraceEvent (see
[http-api.md](http-api.md)), and stored with the turn:
`GET /api/turns/{turnId}/trace` → `{ turnId, conversationId, createdAt, events: TraceEvent[], aguiFrames }` (owner, or
a FIRM_ADMIN of the same firm for turns in the review queue; otherwise 404). Text fields are capped at 20,000
characters and a trace at 1 MB; `truncated: true` marks a capped event. JSON is camelCase.

| kind | data |
|---|---|
| `turn.start` | `{ conversationId, turnId, principal: { userId, firmId, role }, apiInstance, question }` |
| `intent` | `{ intent, forcedRetrieval, forcedTool, stage, model, rawAnswer, durationMs, reason }` — intent ∈ Procedural, Mixed, Data, ChitChat, Other; `stage` ∈ rules, model (the rules are English, a model classifies what they do not recognise); `model`, `rawAnswer` and `durationMs` are null for the rules stage; `reason` explains a model stage that produced nothing usable (timeout, failure, unknown label) |
| `history` | `{ budgetTokens, usedTokens, included: [{ role, text, tokens }], excludedCount }` |
| `prompt` | `{ version, systemPrompt, toolMode, tools: [{ name, description, inputSchema }] }` |
| `model.request` | `{ iteration, model, endpoint, toolMode, temperature, think, tools: [name], messages: [Message] }` |
| `model.response` | `{ iteration, model, text, toolCalls: [{ callId, name, arguments }], finishReason, usage: { inputTokens, outputTokens, totalTokens } \| null, latencyMs }` (durationMs = latency) |
| `tool.forced` | `{ callId, tool, arguments, reason }` — issued on the model's behalf (Ollama ignores tool_choice) |
| `tool.call` | `{ callId, tool, arguments }` — full arguments |
| `tool.result` | `{ callId, tool, isError, latencyMs, mcpInstance, result }` — raw MCP CallToolResult (structuredContent, content, isError) without diagnostics |
| `retrieval` | `{ callId, instance, tenantScope: [tenantId], settings: { mode, fusion, denseVector, limit, prefetchLimit, rerank }, query: { text, original, translated, translationMs, translationNote, terms: [{ term, idf }], denseModel, denseDims }, dense: [Candidate], sparse: [Candidate], fused: [Candidate], rerank: [chunkId] \| null, timings: { embedMs, sparseEncodeMs, qdrantMs, rerankMs } }` |
| `answer.delta` | `{ offset, text }` — the streamed answer, coalesced (≤160 chars or 150 ms per chunk, flushed before tool calls and at the end); offsets are contiguous from 0 and the texts concatenate to the full answer |
| `envelope` | `{ callId, tool, text }` — the exact `<tool_data>` string the model received |
| `tool.unknown` | `{ callId, tool }` — the model asked for a tool that does not exist |
| `audit` | `{ callId, tool, arguments, outcome, durationMs }` — the audit row written (identifiers only) |
| `adjustment` | `{ callId, step, adjustmentId, accountId, amount, currentFee, resultingFee, taskId?, outcome? }` — one per step of a write: `proposed`, `reviewed` (with the A2A task id and the reviewer's outcome), `awaiting_confirmation`. Never the advisor's reason or the reviewer's words |
| `sources` | `{ sources: [{ docId, sectionPath }] }` |
| `signals` | `{ signals: [string] }` |
| `memory` | `{ stored: [{ role, tokens }] }` |
| `turn.end` | `{ durationMs, error, answerChars, toolCalls, sourceCount }` |

`Message` = `{ role, contents: [ { type: "text", text } | { type: "functionCall", callId, name, arguments } | { type: "functionResult", callId, result } ] }`.
`Candidate` = `{ rank, chunkId, docId, tenantId, sectionPath, score }`.

`query.text` is the text that was embedded and encoded: when the question was written in another language it is the
translation into the corpus language, and `query.original` is what the user asked (`translated` says which).
`translationNote` explains a query searched as written despite needing translation (timeout, failure, unusable answer).

Typical order for a procedural question: `turn.start → intent → prompt → history → tool.forced → tool.call →
tool.result → retrieval → audit → envelope → model.request → answer.delta… → model.response → memory → sources → signals →
turn.end`.
The trace is stored before `done` is sent, so the stored copy is readable as soon as the stream ends.

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
