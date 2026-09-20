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

### Requirement: Time-travel controls
The behind-the-scenes monitor SHALL offer time travel over the selected turn's trace:
- a scrubber over all steps (from before the first event to the last);
- step back and step forward;
- jump to start and end;
- play and pause that replay at the recorded timing, with speeds 1×, 2×, 5× and 10× and an option to compress waits
  longer than one second.

Keyboard shortcuts SHALL be ←/→ to step, Space to play or pause, and Home/End to jump. While a turn is streaming, the
cursor SHALL follow the newest step until the user moves it, after which a "Back to live" control SHALL return it.

#### Scenario: Step through a stored turn
- **WHEN** the user opens a stored turn and presses → three times from the start
- **THEN** the cursor is at step 3 and the monitor shows exactly the first three events

#### Scenario: Replay
- **WHEN** the user presses play at 10× from the start of a turn that took 4 seconds
- **THEN** the cursor advances through every step in order and stops at the last step after about 0.4 seconds, plus any compressed waits

#### Scenario: Leave and return to live
- **WHEN** a turn is streaming and the user drags the scrubber back
- **THEN** new events keep arriving without moving the cursor, and "Back to live" jumps to the newest step and resumes following

### Requirement: Monitor views as of a step
Every monitor tab SHALL render only the events up to the cursor, highlight the event at the cursor, and show a
"this step" panel with the cursor event's title, kind, elapsed time and data.

#### Scenario: Retrieval before and after
- **WHEN** the cursor is before the `retrieval` event
- **THEN** the Retrieval tab shows no candidates, and moving the cursor onto that event shows its dense, sparse and fused lists

#### Scenario: Model call in progress
- **WHEN** the cursor is on a `model.request` whose `model.response` comes later
- **THEN** the Model tab shows the request with a "waiting for response" state

### Requirement: Chat as of a step
When the cursor of the selected turn is before its last step, the chat pane SHALL show that turn as it was at the
cursor:
- the answer text reconstructed from the `answer.delta` events up to the cursor;
- tool cards in their running or finished state;
- sources only once the `sources` event is reached;
- a banner "viewing step k of N" with a control to return to the present.

Other turns in the chat SHALL be unaffected.

#### Scenario: Rewind the answer
- **WHEN** the cursor is on the first `answer.delta` event of a turn
- **THEN** the chat shows only that chunk of the answer for that turn, with the time-travel banner

#### Scenario: Tool card rewinds
- **WHEN** the cursor is between a `tool.call` and its `tool.result`
- **THEN** the chat shows that tool card in the running state

### Requirement: Topology screen
The `/topology` screen SHALL render the drawn diagram of the stack with its live state on top. Each node SHALL show
its health (healthy, degraded, unreachable) in a way that does not rely on colour alone, its replica instances, and
the facts reported for it; each edge SHALL be drawn as the diagram draws it. The screen SHALL be reachable from the
main navigation and open to any signed-in user.

The screen SHALL refresh the state while it is open, SHALL say when the state was last refreshed, and SHALL offer a
manual refresh. Selecting a node SHALL show its full reported detail. When the state cannot be loaded, the diagram
SHALL still be shown, with an error telling the user the state is unknown rather than an empty screen.

#### Scenario: Live state on the diagram
- **WHEN** a signed-in user opens `/topology` with the stack running
- **THEN** every node is drawn in its place and marked healthy, and api and the MCP server show their replicas

#### Scenario: A service goes down while the page is open
- **WHEN** a service stops and the page refreshes
- **THEN** that node changes to unreachable with its reason, and the rest of the diagram is unchanged

#### Scenario: Node detail
- **WHEN** the user selects a node
- **THEN** the screen shows everything reported for it, including instances, versions and facts

#### Scenario: State cannot be loaded
- **WHEN** the topology request fails
- **THEN** the diagram is still rendered, with an error saying the live state is unavailable

#### Scenario: Not signed in
- **WHEN** no persona is selected
- **THEN** the screen says a persona must be picked, as the other screens do

### Requirement: Compliance screen
The `/admin/compliance` screen SHALL be available to a FIRM_ADMIN and SHALL show three things for their own firm:
the state of the audit chain, the record of actions, and a way to produce an export.

The chain state SHALL be stated in words, not only in colour: whether it is intact, how many records were checked,
how many predate the chain, and — when it is broken — which record broke it and when. A broken chain MUST be
unmistakable, and the screen SHALL say that records before the break are unaffected.

The record SHALL be shown newest first with the time, the person, the kind, the action and its outcome, filterable
by person, kind and period, with a way to load older records. The screen MUST NOT imply that identifiers are the
whole story: where an action carries no arguments, it SHALL show that plainly rather than an empty cell.

The export SHALL take a period and, optionally, a person, download the package as a file, and then show the
manifest — the counts, the digest and the chain head — so it can be quoted without opening the file.

The screen SHALL NOT claim more than the system provides: it SHALL state that the chain detects tampering rather
than preventing it.

#### Scenario: Intact chain
- **WHEN** a FIRM_ADMIN opens the screen and the chain is intact
- **THEN** it says so in words, with how many records were checked and how many predate the chain

#### Scenario: Broken chain
- **WHEN** the chain is broken
- **THEN** the screen shows it unmistakably, names the record that broke it and the time, and says that earlier records are unaffected

#### Scenario: Browsing and filtering
- **WHEN** the admin filters by a person and loads more
- **THEN** only that person's actions are listed, newest first, and older ones are appended

#### Scenario: Export
- **WHEN** the admin exports a period
- **THEN** the package downloads as a file and the manifest's counts, digest and chain head are shown on screen

#### Scenario: Nothing recorded yet
- **WHEN** the record is empty for the chosen filters
- **THEN** the screen says so rather than showing an empty table

#### Scenario: Not an admin
- **WHEN** a user who is not a FIRM_ADMIN opens the screen
- **THEN** it shows the same access-denied treatment as the other admin screens
