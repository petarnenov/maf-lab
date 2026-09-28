# Design

## Context

Each turn: `JevIntentClassifier` sends one request (intent Choice + `in_domain` Noul, state `{user_question}`), then
`ChatTurnRunner` sets `ToolMode` — `RequireSpecific("search_documents")` for procedural/mixed in-domain intents, `Auto`
otherwise — and runs the `ChatClientAgent`. `RequiredToolModeChatClient` sits under `FunctionInvokingChatClient` and,
while the required tool has not been called, answers the model call itself with a synthetic `FunctionCallContent`
(`search_documents`, `query` = the question); the function-invoking layer executes it over MCP and clears the required
mode, so the next call is the model answering. A data turn today makes two model calls: one to choose a tool (median
652 ms in the analysis traces), one to answer.

Planning probe (this change, `jev-1.13.0`, 35 questions: the 24 of `evals/selection.jsonl` + the 11 non-English
data/mixed/write questions of `evals/intent.jsonl`), the production request compared with two ways of adding the tool
questions:

| form | intent or in_domain moved (vs. production request) | p50 latency |
|---|---|---|
| tools described in the **state** (the analysis probe's form) | **22 / 35** (in_domain jumps to ~0.98 everywhere; s-22 `mixed` 0.73 → 0.46, below the floor) | 525 ms |
| tools described in each Noul's **structured instructions** | **2 / 35** (in_domain 0.60→0.69, 0.76→0.81; no choice changed) | 525 ms |

In the instructions form, for every question Jev classified `data` (7 English, 5 non-English), the higher of the two
read-tool probabilities named the expected tool (0.73–0.93); the other read tool scored high too on run-id questions
(0.72–0.83), so the probabilities alone do not separate "status of run 4417" from "list runs" — but the run id does.

## Goals / Non-Goals

**Goals:** remove the tool-choosing model call on data turns where the choice is unambiguous; keep one Jev request per
turn; leave intent and domain answers as they are; never route a write; fail open.

**Non-Goals:** routing mixed or procedural turns (they already force `search_documents`, and a model adds the status
call); multi-tool data questions; parsing relative dates ("last month") — those go to the model; the A2A bridge.

## Decisions

### D1. Routing questions in the same request, described in instructions

With `Jev:RouteDataTools` on, the request adds `tool_get_billing_run_status`, `tool_search_billing_runs`,
`tool_propose_fee_adjustment` — Nouls whose structured instructions are `{ tool: "<name>: <what it returns>",
question: "To answer `user_question`, is it necessary to call `tool`?" }` — and `run_status`, a Choice over pending,
running, completed, failed, none. The state stays `{user_question}` (probe above: the state form moves the intent).
With routing off, the request is exactly today's. `search_documents` gets no Noul: the intent decides it.

### D2. The routing rule (code, after the intent decision)

Only when the turn's intent is `Data` and was used (confidence ≥ `MinConfidence`):

1. `tool_propose_fee_adjustment` ≥ 0.5 → not routed ("a write is indicated").
2. The read tool with the higher probability is the candidate; below `Jev:MinRouteProbability` (0.8) → not routed.
3. Arguments by code: run ids are `run|рън|ран` followed by a number of three or more digits, or `#` followed by one.
   `get_billing_run_status` needs exactly one distinct id → `{runId}`. `search_billing_runs` needs none; `status` from
   `run_status` when it is not `none` and its confidence ≥ `MinConfidence`; `periodFrom`/`periodTo` from "<month>
   <year>" (English, Bulgarian Cyrillic and Latin month names) as the month's first and last day. A question with a
   time expression the code does not parse (a year alone, "last month", "Q2", a date) → not routed.
4. A missing or malformed routing answer → not routed. Every "not routed" keeps its reason for the trace.

The run-id cross-check makes the rule robust to the read tools' overlapping probabilities, and the probability floor
keeps Jev's judgment in charge: code never routes a question Jev did not tie to that tool.

### D3. Issuing the call

`IntentDecision` gains `Route` (tool, arguments, probability) and `RouteReason`. `ChatTurnRunner` sets
`ToolMode = RequireSpecific(route.Tool)` for a routed turn; `RequiredToolModeChatClient` takes the route and, while
that tool has not been called, returns it as the synthetic call (`search_documents` keeps today's behaviour). The
wrapper is installed when emulation is on or a route exists. The function-invoking layer executes it over MCP — the
same audit, trace, envelope and tenant path as a model-chosen call — and the model answers next with the result in
context, free to call further tools. The `tool.forced` trace event names the reason ("data intent routed by Jev").

### D4. Writes and safety

`propose_fee_adjustment` is not in the set of routable tools at all; its Noul only vetoes. Arguments come from the
user's own words through fixed patterns and go through the tool's own validation and the principal's tenant scope, so a
routed call can do nothing a model-chosen call could not. Adversarial text can at most make Jev route or not route a
read.

### D5. Decision rule, fixed before measuring

Ship `RouteDataTools` on only if the selection eval (≥ 3 runs) keeps recall and negativeAccuracy at the baseline (1.0 /
1.0), exactMatch inside its observed noise (0.917–1.0), and the kept traces show the first model call removed on routed
data turns, with the latency difference measured. Otherwise ship it off.

## Risks / Trade-offs

- [Jev degraded] → the intent request's existing 2 s budget and fail-open cover the new questions; a timeout routes
  nothing and forces nothing, as today.
- [Intent answers drift because the request changed] → instructions form measured at 2/35 small drifts; the `intent`
  eval suite is re-run with routing on.
- [Wrong routed tool] → the model still sees the result and may call the right tool; the run-id cross-check and the
  floor keep this rare. Measured by selection exactMatch.
- [A data question in words the regex does not know] → not routed; the model picks as today.

## Migration Plan

Configuration only; `Jev__RouteDataTools=false` restores today's request and behaviour.

## Open Questions

None.
