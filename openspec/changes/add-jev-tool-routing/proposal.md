# Proposal

## Why

On a data turn ("status of run 4417", "which runs failed?") the first model call does one thing: pick a read tool and
fill its arguments. In the traces behind the Jev analysis (2026-09-28) that call took 589–903 ms (median **652 ms**)
before any data was fetched, and the answer needed a second call anyway. The intent request Jev already answers on every
turn knows the question is `data`; a few more questions in the same request can name the read tool, and the arguments
of those two tools are a run id and a status — regex and a Choice, not generation. The analysis probe routed 20/24
selection questions to exactly the expected tools (p50 296 ms, one request each); the four misses were questions that
today's intent forcing already sends to `search_documents`.

## What Changes

- When routing is enabled, the one intent request per turn also asks, as structured instructions that leave the
  question's state untouched: one Noul per read tool ("to answer `user_question`, is it necessary to call `tool`?"), a
  Noul for the write tool (used only as a veto), and a Choice for the run status a question asks about.
- A **data** turn whose intent is used is pre-routed when exactly one read tool is indicated with enough confidence
  and its arguments can be taken from the question by code: `get_billing_run_status` needs exactly one run id;
  `search_billing_runs` takes the status Choice and a "Month YYYY" period, and is not routed when the question holds a
  time expression code cannot parse. Otherwise — or when the write tool's probability is high — the turn behaves as
  today: the model picks.
- A pre-routed call is issued on the model's behalf, the way the forced `search_documents` call is today; the model's
  first call is skipped and it answers with the result in context (it may still call further tools).
- `propose_fee_adjustment` is never pre-routed: a write stays with the model and its confirmation flow.
- The intent trace event records the routing answer (per-tool probabilities, status, the chosen tool and arguments, or
  why the turn was not routed); the pre-routed call appears as a tool call issued on the model's behalf.
- Routing is configurable (`Jev:RouteDataTools`, `Jev:MinRouteProbability`) and fails open: a routing answer that is
  missing or unusable leaves the turn exactly as today. Whether it ships on is decided by the selection eval against
  rules fixed before measuring.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `intent-classification`: the typed request carries the routing questions when routing is on (still one request per
  turn); a new requirement defines when a data question is routed and to what.
- `chat-agent`: a new requirement — a routed data turn's read call is issued without the first model call; writes are
  never routed.
- `turn-tracing`: the intent event carries the routing answer; a scenario for a routed data turn.

## Impact

- `src/Maf.Lab.Api/Agent/Jev/JevIntentClassifier.cs`, `IntentClassifier.cs` (decision gains a route), a small
  argument extractor, `RequiredToolModeChatClient.cs` (issues a routed call, not only `search_documents`),
  `ChatTurnRunner.cs` (tool mode and trace).
- `Maf.Lab.Retrieval/Jev/JevOptions.cs` (routing options; the class moves there in `add-jev-passage-relevance`).
- `tests/Maf.Lab.Tests/FakeJev.cs`, `compose/ollama-stub/server.py` — answer the routing questions.
- `DECISIONS.md` — probe, eval runs, latency, the decision. No package added.
- Cost: four more small questions in the intent request (latency unchanged in the probe: p50 525 vs 524 ms).
