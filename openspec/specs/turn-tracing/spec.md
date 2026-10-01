# turn-tracing Specification

## Purpose
Captures a complete, structured trace of everything the system does for a chat turn, streams it live to the user who
asked, and keeps it for later inspection, so the internals of the agent and retrieval can be observed and learned from.

## Requirements

### Requirement: Complete turn trace
For every chat turn the system SHALL record an ordered trace of timestamped events. The trace MUST cover:
- turn start: principal, conversation, turn id, api replica, question;
- intent classification: the intent the turn proceeded with, whether retrieval was forced, the classifier's choice,
  its probability for every known intent and its confidence, the probability that the question is about the documented
  domain, the versioned model that answered, how long the classification took, and — when the turn proceeded with no
  recognised intent because of the classifier — why (below the confidence floor, outside the domain, timed out,
  rejected, unavailable). When tool routing is enabled it SHALL also carry the routing answer: each routing question's
  probability, the status answer, and either the routed tool with its arguments or why the turn was not routed. It
  MUST NOT contain any credential;
- the history window: included messages with roles, text and token counts, the token budget, and how many older
  messages were left out;
- the system prompt version and full text, and each offered tool with its description and input schema;
- every model call: iteration, full request messages and options (tool mode, temperature, reasoning setting), response
  text, requested tool calls, finish reason, input and output token usage when the provider reports it, latency,
  model and endpoint host;
- any tool call issued on the model's behalf — to force retrieval, or a routed read — with the reason it was issued;
- every tool call: full arguments, raw MCP result, error flag, latency and serving MCP replica;
- the exact data envelope the model received;
- the model's reasoning as ordered chunks (`reasoning.delta`, each with its character offset), coalesced from the
  reasoning the model streamed; concatenating them yields exactly what it reasoned. A model that reasons in
  several stretches across a turn SHALL have each recorded in the order it arrived;
- the streamed answer text as ordered chunks (`answer.delta`, each with its character offset). Chunks are coalesced
  from the model's text deltas, and concatenating them yields exactly the answer sent to the client;
- audit rows written, sources, production signals, memory rows stored, and turn end with total duration and any error.

#### Scenario: Procedural turn trace
- **WHEN** a user asks "What is the procedure when a fee schedule is missing?"
- **THEN** the trace contains, in order: turn start, intent (procedural, forced, with the classifier's confidence), history, prompt and tools, the forced search call, the MCP result with retrieval diagnostics, the envelope, at least one model call with its response, sources, signals, and turn end

#### Scenario: Intent decided by the model
- **WHEN** Jev classifies the question
- **THEN** the intent event names the versioned model, the choice, the probability of each known intent, the confidence and the duration

#### Scenario: Classification not used
- **WHEN** the classifier's confidence is below the floor, or it times out, fails or has no key
- **THEN** the intent event shows no recognised intent, nothing forced, and the reason

#### Scenario: Unknown tool attempt
- **WHEN** the model calls a tool that does not exist
- **THEN** the trace records the attempt, the refusal returned to the model, and the audit row

#### Scenario: Answer reconstructable from the trace
- **WHEN** a turn streams an answer
- **THEN** the `answer.delta` events have contiguous offsets starting at 0, and their texts concatenated equal the full answer

#### Scenario: Reasoning reconstructable from the trace
- **WHEN** a turn's model reasons before it answers
- **THEN** the `reasoning.delta` events have contiguous offsets starting at 0, their texts concatenated equal
  everything the model reasoned, and they are recorded before the model response that followed them

#### Scenario: A turn whose model does not reason
- **WHEN** a model answers without reasoning
- **THEN** the trace holds no `reasoning.delta` event and the rest of the trace is unchanged

#### Scenario: Reasoning stays out of the logs
- **WHEN** a turn's model reasons
- **THEN** no log line contains any of that reasoning

#### Scenario: Question outside the domain
- **WHEN** a procedurally-phrased question is outside the documented domain
- **THEN** the intent event shows the classifier's procedural choice, the in-domain probability, no recognised intent, nothing forced, and the reason

#### Scenario: Routed data turn
- **WHEN** routing is on and the user asks "status of run 4417"
- **THEN** the intent event shows the data intent, each routing probability and the routed tool with run id 4417, the routed call is recorded as issued on the model's behalf before any model call, and exactly one model call follows it

### Requirement: Live streaming of the trace
A turn's trace SHALL be readable while the turn runs, so that each step is visible when it happens. It SHALL NOT travel
on the run's AG-UI stream. The owner of the turn SHALL be able to read the trace recorded so far, and every event
recorded after a given position, from the trace API, under the same access rules as the stored trace. The run's own
progress SHALL travel on the stream as the protocol's steps (`STEP_STARTED`/`STEP_FINISHED`), named by what the turn
is doing (for example screening the question, calling a tool, checking the answer), so a client that never reads the
trace still sees what the turn is doing.

#### Scenario: Trace arrives before the answer completes
- **WHEN** a turn calls `search_documents` and the monitor is open
- **THEN** the monitor shows the trace events for that tool call before the run's terminal event

#### Scenario: A consumer that ignores the trace
- **WHEN** a client follows only the run's AG-UI stream
- **THEN** the run renders fully from the protocol's own events, and its steps show what the turn is doing

#### Scenario: The stream carries no trace
- **WHEN** any turn runs
- **THEN** no event on its AG-UI stream carries a trace event

#### Scenario: Another user's running turn
- **WHEN** a user asks for the live trace of a turn they do not own
- **THEN** it is reported as not found

### Requirement: Persistence and retention
The complete trace of each turn SHALL be stored with the turn and returned by `GET /api/turns/{turnId}/trace`. Traces
older than the configured retention (default 7 days) SHALL be deleted automatically.

#### Scenario: Reopen an old turn
- **WHEN** a user selects an earlier assistant turn of their conversation
- **THEN** its stored trace is shown

#### Scenario: Retention
- **WHEN** the retention job runs and a trace is older than the retention period
- **THEN** that trace is deleted and requesting it returns not found

### Requirement: Access scope
A trace MUST only be readable by the user who owns the turn and, for turns in their firm's review queue, by a
FIRM_ADMIN of the same firm. Users of other firms MUST receive not found. Trace data MUST NOT be written to logs.

#### Scenario: Other user
- **WHEN** a different user of the same firm (not FIRM_ADMIN) requests another user's trace
- **THEN** the response is not found

#### Scenario: Other firm's admin
- **WHEN** a FIRM_ADMIN of firm B requests a trace of a firm A turn
- **THEN** the response is not found

#### Scenario: Logs stay clean
- **WHEN** a traced turn completes
- **THEN** no log line contains the question, the answer, snippets or prompt text

### Requirement: Size limits
Individual text fields in a trace SHALL be capped at 20,000 characters and a whole trace at 1 MB. Truncation MUST be
marked in the trace.

#### Scenario: Oversized field
- **WHEN** a field exceeds the cap
- **THEN** it is truncated and flagged as truncated in the event

### Requirement: The run's AG-UI frames are recorded
For every run it streams, the system SHALL record each event it puts on the wire, in the order it was written, as
a frame carrying: its position in the run, the milliseconds since the run started, the protocol event type, the
payload's size in bytes, and the payload itself. The record SHALL cover every event of the run, including those a
client may ignore — the run starting, a text message opening and closing, a tool call ending, a step — and the run's
terminal event.

The frame log SHALL be capped at 256 KB per run. Once the cap is reached, later frames SHALL still be recorded
with their position, timing, type and size, and SHALL be marked as truncated in place of their payload.

#### Scenario: Every event of a run is recorded
- **WHEN** a turn answers a question
- **THEN** the run's frame log holds, in order, the run-started frame, each step frame, the text message's start,
  content and end frames, each tool-call frame, each activity or state frame, and the terminal frame

#### Scenario: A trace frame is recorded by reference
- **WHEN** a turn records a trace event
- **THEN** no frame is recorded for it, because the trace no longer travels on the stream; the trace itself keeps it

#### Scenario: A run that exceeds the cap
- **WHEN** a run's frames exceed 256 KB
- **THEN** the frames past the cap are recorded with their position, timing and type, and are marked truncated

### Requirement: The frames are kept and served with the turn's trace
The frames of a run that produced a turn SHALL be stored with that turn and returned by
`GET /api/turns/{turnId}/trace` together with the trace's events. They SHALL be readable exactly by whoever may
read that turn's trace, and SHALL be deleted when that trace is deleted, whether by retention or otherwise. A run
that produces no turn — an answer to a confirmation — SHALL NOT have its frames stored, and the response for a
turn that has none SHALL say that they were not recorded rather than report an empty run.

#### Scenario: Reopen a stored turn
- **WHEN** a user opens an earlier assistant turn of their conversation
- **THEN** the trace response carries that turn's recorded frames alongside its trace events

#### Scenario: Another firm's admin
- **WHEN** a FIRM_ADMIN of firm B requests a firm A turn's trace
- **THEN** the response is not found, and no frame of that run is disclosed

#### Scenario: Retention
- **WHEN** the retention job deletes a turn's trace
- **THEN** that turn's frames are deleted with it

#### Scenario: A run with no turn
- **WHEN** a run answers a confirmation and so records no turn
- **THEN** its frames are not stored, and no stored turn reports them

#### Scenario: Frames stay out of the logs
- **WHEN** a run's frames are recorded
- **THEN** no log line contains a frame's payload

### Requirement: Guard decisions are traced
Every screening a turn performs SHALL be recorded in its trace as a `guardrail` event: which check it was (the prompt, a
tool result, a reviewer's words), the tool and call it concerned when there was one, the probability of every question
asked for every item assessed, the decision (pass, blocked, withheld or unscreened), the threshold it was taken
against, how many items were withheld, the versioned model that answered, how long it took and — when the text was
unscreened — why. For a codebase search it SHALL also record which context the items were screened in and which
questions were record-only, so a record-only score at or above the threshold is visible without having withheld
anything. The event MUST NOT contain the assessed text a second time, nor any credential.

When the guard blocks or withholds, the turn SHALL NOT perform or record the downstream step for the blocked or
withheld item: the trace holds only the guard's decision and, where the model needs one, a neutral notice — never the
blocked prompt, the withheld content, or the work that would have followed it. Concretely:
- A turn whose prompt is blocked calls no model and runs no tool. Its trace SHALL hold turn start, intent, the
  `guardrail` event, the refusal as `answer.delta`, sources, signals and turn end, and it SHALL NOT hold the `prompt`
  event (the system prompt text and the tool schemas), nor any history, tool-call or data-envelope event — neither in
  the live stream nor in the stored trace.
- A withheld tool-result item SHALL be absent from the `tool.result` event as well as from the model's data envelope
  and the turn's sources: the recorded result SHALL be the redacted one the model may read — for a search, the stub
  that keeps only the item's non-text identifiers (a path and line range, or a document id) and the neutral notice
  with a count — never the withheld excerpt, snippet, symbol, section heading or record.
- A flagged reviewer's or other agent's words SHALL NOT be recorded in the trace, and the trace SHALL show only the
  neutral failed-review outcome.
- A blocked A2A partner prompt SHALL be refused with no tool fetch, no prompt build and no model call, and SHALL leave
  no trace of the blocked question.

#### Scenario: A refused prompt
- **WHEN** a prompt is refused
- **THEN** the trace holds a `guardrail` event for the prompt with the scores, the decision `blocked` and the threshold, and no model call

#### Scenario: A blocked prompt omits the system prompt and tools
- **WHEN** a prompt is blocked
- **THEN** neither the streamed trace nor the stored trace holds a `prompt` event, so it carries no system prompt text and no tool schema, and it holds no history or envelope event either

#### Scenario: A withheld excerpt
- **WHEN** one excerpt of a search result is withheld
- **THEN** the trace holds a `guardrail` event for that tool call with each excerpt's scores, the decision `withheld` and one withheld item, and the envelope event shows what the model received instead

#### Scenario: A withheld excerpt's content stays out of the trace
- **WHEN** one excerpt of a search result is withheld
- **THEN** the withheld excerpt's text appears in neither the `tool.result` event nor the envelope event, which carry the stub and the neutral notice instead

#### Scenario: A record-only score on a codebase snippet
- **WHEN** a codebase snippet scores 0.97 on the record-only question and passes
- **THEN** the `guardrail` event records the codebase context, the record-only question, the 0.97 and the decision `pass`

#### Scenario: A flagged reviewer's words stay out of the trace
- **WHEN** a compliance reviewer's reason or question is flagged by the guard
- **THEN** the trace records the review as failed with a neutral outcome and holds none of the reviewer's flagged words

#### Scenario: A blocked partner leaves no trace of its question
- **WHEN** an A2A partner's question is blocked by the guard
- **THEN** the partner receives the fixed refusal with no model call, and no trace records the blocked question, the system prompt or the tool schemas

#### Scenario: Screening unavailable
- **WHEN** a screening request times out
- **THEN** the `guardrail` event records the decision `unscreened` and the reason

### Requirement: Jev relevance judgments are traced
Every search in a turn that asked Jev for a relevance judgment SHALL add a `relevance` event to the turn's trace,
after that search's tool result. It SHALL be recorded whether or not retrieval diagnostics were requested.

The event SHALL carry:
- the tool call it concerns;
- whether the gate was on;
- the floor;
- how many candidates were judged;
- the highest probability;
- whether the gate silenced the search;
- whether Jev's answer ordered the results;
- the versioned model;
- how long the judgment took;
- when Jev did not answer, why.

Its duration SHALL be the judgment's duration. Its title SHALL name Jev and say the outcome in one of three ways:
- **silenced:** the highest probability is below the floor;
- **kept:** the highest probability is at or above the floor, or the gate is off;
- **ungated:** Jev was unavailable, with the reason.

The event MUST NOT contain the query, a passage, a chunk id, or any credential.

A search that did not ask Jev SHALL add no `relevance` event.

#### Scenario: A search the gate kept
- **WHEN** a turn's search is judged with a highest probability of 0.87 against a floor of 0.5
- **THEN** the trace holds a `relevance` event for that call whose title names Jev, 0.87, the floor and "kept", and
  whose duration is the judge's latency

#### Scenario: A search the gate silenced
- **WHEN** every judged candidate's probability is below the floor
- **THEN** the `relevance` event says the search was silenced, with the highest probability and the floor

#### Scenario: Jev unavailable
- **WHEN** the relevance request times out
- **THEN** the `relevance` event says the search was left ungated and gives the reason

#### Scenario: Diagnostics not requested
- **WHEN** retrieval diagnostics are turned off and a search asks Jev for a judgment
- **THEN** the trace still holds the `relevance` event for that search, and holds no `retrieval` event

#### Scenario: No judgment asked
- **WHEN** a search runs with the relevance gate off and a reranker other than Jev's
- **THEN** the trace holds no `relevance` event for it

### Requirement: A screening's Jev requests are counted
A `guardrail` event for a tool result SHALL record:
- how many Jev requests the screening made (one per non-empty item);
- as its duration, the time from the first request to the last answer.

The latency of each item SHALL stay in the event. When the screening made more than one request, the title SHALL say
how many.

#### Scenario: A search result with five excerpts
- **WHEN** a search result with five non-empty excerpts is screened
- **THEN** the `guardrail` event records five requests, each excerpt's own latency, and a duration covering the whole
  screening, and its title mentions the five requests

#### Scenario: A single screened result
- **WHEN** a tool result other than a document search is screened
- **THEN** the `guardrail` event records one request, and its title does not mention a request count

### Requirement: The domain boundary is traced
The turn trace SHALL show where a question sits among the domains and where the turn crossed between them.
- **`domain` event:** right after `intent`, when Jev answered. It SHALL carry each domain's probability, the scope
  floor, the domains in scope, the primary domain, whether the question crosses, and the searches forced.
- **Tool events:** every `tool.forced`, `tool.call` and `tool.result` SHALL carry the `domain` and `server` of its
  tool.
- **`boundary` event:** each time a tool call's domain differs from the domain of the previous tool call in the turn,
  naming the two domains and the tool.
- **`turn.end`:** SHALL carry:
  - the turn's domain path, with consecutive repeats collapsed;
  - the domains it touched;
  - the domains Jev predicted.

#### Scenario: Crossing turn
- **WHEN** a turn calls `search_documents` and then `get_aum_history`
- **THEN** the trace has a `boundary` event from billing to portfolio, and `turn.end` has the domain path `["billing", "portfolio"]`

#### Scenario: Single-domain turn
- **WHEN** a turn calls only billing tools
- **THEN** there is no `boundary` event and the domain path is `["billing"]`

### Requirement: The answer check is traced
A turn whose answer was checked by Jev (as specified by `answer-check`) SHALL record one `answer.check` event, before
the turn's sources, signals and end. It SHALL carry the relevance and grounding probabilities, both signal floors
(`relevantFloor`, `groundedFloor`: below them a review signal is raised), both pass thresholds, the verdict (`pass`,
`uncertain`, `not_relevant`, `not_grounded`, `unchecked`), the context it was asked in (billing or codebase), the
versioned model, the reason when unchecked, how many sources and characters were judged, how many previous sources,
how many duplicates were dropped, and how many Jev requests the check made. The event's duration SHALL be the check's
latency, so the timeline draws it as a bar of its own. Its title SHALL name Jev, both probabilities against their
band and the verdict, or, when unchecked, the reason. The event SHALL NOT contain the answer's text or the sources'
text. A turn that ran no check SHALL record no `answer.check` event.

#### Scenario: A checked answer in the trace
- **WHEN** a procedural turn is answered and Jev checks the answer
- **THEN** its trace holds an `answer.check` event after the model's response and before `sources`, `signals` and `turn.end`, with both probabilities, both floors, both pass thresholds, the verdict and a duration
- **AND** its title reads like "Jev answer check: relevant 0.95 ≥ 0.80, grounded 0.95 ≥ 0.80 — pass"

#### Scenario: An uncertain answer in the trace
- **WHEN** Jev's grounding probability is 0.35 inside the review band
- **THEN** the `answer.check` event's verdict is `uncertain`, its title reads like "… grounded 0.35 in 0.20–0.80 — uncertain", and the `signals` event carries no answer signal

#### Scenario: The event holds no content
- **WHEN** a turn's answer is checked
- **THEN** the `answer.check` event contains neither the answer's text nor any excerpt's text

#### Scenario: Unchecked is recorded with its reason
- **WHEN** the answer check times out
- **THEN** the `answer.check` event's verdict is `unchecked` and its reason says it timed out

#### Scenario: Over the cap is recorded
- **WHEN** a turn's deduplicated sources do not fit under the cap
- **THEN** the `answer.check` event's verdict is `unchecked`, its reason is `sources over cap`, and its `requests` is 0

### Requirement: The trace says which domains' tools were loaded
The `domain` event SHALL record the domains whose tools the turn loaded. It SHALL also record the reason:
- `in scope`: the classification's domains;
- `conversation`: the conversation's stored domains, for a follow-up;
- `all`: no domain verdict.

It SHALL record the conversation's stored domains before the turn.

#### Scenario: A follow-up
- **WHEN** a follow-up in no domain loads the conversation's portfolio tools
- **THEN** the `domain` event records `loaded: [portfolio]` with the reason `conversation`
