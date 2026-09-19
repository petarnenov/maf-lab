# web-ui Specification

## Purpose
Provides the browser interface for chatting with the assistant, seeing its
tool use and sources, giving structured feedback, and operating indexing,
evals, and the feedback review queue.

## Requirements

### Requirement: Streaming chat with visible tool use
The `/chat` screen SHALL render the assistant's answer as it streams, show a
live card for each tool call (for example "searching documentation…",
"checking run 4417") that updates when the call finishes, and show a sources
panel with clickable sections.

#### Scenario: Tool card lifecycle
- **WHEN** a `tool_call_started` event arrives followed by `tool_call_finished`
- **THEN** a card appears in a running state and then shows the result summary and source count

#### Scenario: Sources panel
- **WHEN** a `sources` event arrives
- **THEN** each source is listed with its section path and a way to view its snippet

### Requirement: Structured feedback on every answer
Each assistant turn SHALL offer three feedback actions — wrong tool, wrong
document, wrong answer — each posting a structured feedback event that
identifies the turn, the tool calls, and the sources.

#### Scenario: Wrong document
- **WHEN** the user clicks "wrong document" under an answer
- **THEN** a feedback event is stored that the eval harness can import as a labeled retrieval row

### Requirement: Evals screen
The `/evals` screen SHALL list recent runs of the selection, retrieval, and
generation (and injection) evals with their metrics over time.

#### Scenario: Reports listed
- **WHEN** eval reports exist
- **THEN** the screen shows each run's date, suite, mode and metrics in a table

### Requirement: Index administration screen
The `/admin/index` screen SHALL let an admin trigger indexing, view drift
percentage, view the distribution of model_version across chunks, and start
the embedding migration. It SHALL be available only to FIRM_ADMIN.

#### Scenario: Non-admin
- **WHEN** an ADVISOR opens `/admin/index`
- **THEN** access is denied

### Requirement: Feedback review queue
The `/admin/feedback` screen SHALL list turns flagged by production signals —
negative feedback, rephrased question, no tool on a how/why question, zero
retrieval results, long answer without sources — and provide a labeling form
whose submission appends a row to the matching eval dataset.

#### Scenario: Zero-result turn flagged
- **WHEN** a turn's `search_documents` call returned zero results
- **THEN** the turn appears in the review queue with the signal "zero retrieval results"

#### Scenario: Label appended
- **WHEN** a reviewer submits a label for a flagged turn
- **THEN** a row is appended to the corresponding eval dataset

### Requirement: Two-pane chat with behind-the-scenes monitor
The `/chat` screen SHALL show the conversation in a left pane and a behind-the-scenes monitor in a right pane. On
screens narrower than 1024 px the monitor SHALL stack below the conversation. The monitor SHALL follow the turn that
is streaming, and SHALL switch to a past assistant turn when the user selects it.

#### Scenario: Layout
- **WHEN** a user opens `/chat` on a wide screen
- **THEN** the chat is on the left and the monitor on the right

#### Scenario: Select a past turn
- **WHEN** the user clicks an earlier assistant turn
- **THEN** the monitor loads and shows that turn's stored trace

### Requirement: Monitor views
The monitor SHALL present the turn trace as:
- a timeline of all events with elapsed times and durations;
- a model-calls view showing each request (messages, tools, tool mode) and response (text, tool calls, tokens, latency);
- a retrieval view showing tenant scope, settings, query terms, and dense, sparse and fused candidates with scores and
  rerank order;
- an MCP view with raw arguments and results and the serving replica;
- a prompt and memory view with the system prompt, tool schemas and history window.

Every event's raw data SHALL be expandable.

#### Scenario: Retrieval internals visible
- **WHEN** a turn's search_documents call completes
- **THEN** the retrieval view lists the dense, sparse and fused candidates with chunk ids and scores for that call

#### Scenario: Live timeline
- **WHEN** a turn is streaming
- **THEN** new timeline entries appear as their trace events arrive

### Requirement: Trace from the review queue
The `/admin/feedback` review form SHALL offer the turn's trace to the reviewing FIRM_ADMIN.

#### Scenario: Reviewer opens trace
- **WHEN** a FIRM_ADMIN opens a flagged turn in the review queue
- **THEN** a link or panel shows that turn's trace
