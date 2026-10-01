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
panel with clickable sections. It SHALL render from the run's own events, and an
event it does not recognise SHALL leave the rest of the run rendering.

#### Scenario: Tool card lifecycle
- **WHEN** a tool call starts and later reports its result
- **THEN** a card appears in a running state and then shows the result summary and source count

#### Scenario: Sources panel
- **WHEN** a run reports the sources of its answer
- **THEN** each source is listed with its section path and a way to view its snippet

#### Scenario: An event the screen does not know
- **WHEN** a run carries an event this client has no rendering for
- **THEN** the answer, the tool cards and the sources still render

### Requirement: Structured feedback on every answer
Each assistant turn SHALL offer four feedback actions — wrong tool, wrong
document, wrong answer, and wrong confirmation summary — each posting a
structured feedback event that identifies the turn, the tool calls, and the
sources. The fourth SHALL be offered only on a turn that asked for a
confirmation, since it is about what that summary said.

#### Scenario: Wrong document
- **WHEN** the user clicks "wrong document" under an answer
- **THEN** a feedback event is stored that the eval harness can import as a labeled retrieval row

#### Scenario: Wrong confirmation summary
- **WHEN** the user clicks "wrong confirmation summary" under a turn that proposed a write
- **THEN** a feedback event of that kind is stored for that turn

#### Scenario: Not offered where it makes no sense
- **WHEN** a turn asked for no confirmation
- **THEN** the fourth action is not offered

### Requirement: Evals screen
The `/evals` screen SHALL list recent runs of the selection, retrieval, and
generation (and injection) evals with their metrics over time.

For a chosen suite and metric, the screen SHALL show how it moved across past runs, with the accepted baseline
marked, so a slow decline is visible rather than inferred from a table. Where a run recorded a comparison with the
baseline, the screen SHALL show what regressed, what improved and what was new, in words and figures — never by
colour alone.

The metric picker SHALL group its series by suite and variant, one labelled group per suite and variant, and SHALL
derive the groups from the series the reports contain, so a suite, variant or metric that first appears in a new run
lands in its group without a change to the screen. Groups SHALL be ordered by suite — selection, retrieval,
generation, injection, confirmation, intent, a2a-conformance, then any other suite alphabetically — and, within a
suite, by variant, the production variant first (for retrieval: hybrid, hybrid-dbsf, dense, sparse), then any other
variant alphabetically. Within a group, a metric that has breakdowns (a `metric:part` series) SHALL come first, followed
by the other metrics of its family (the same name before `@`, by ascending number), then the remaining metrics
alphabetically; each metric's breakdowns SHALL follow it directly, languages first (en, bg, bg-latn, then any other
language code) and other splits after them alphabetically. An option inside a group SHALL name only the metric and
its breakdown, the chosen series' suite and variant SHALL stay visible while the picker is closed, and choosing a
series SHALL still identify it by its full suite, variant and metric. At phone width the picker SHALL fit the screen,
and the runs table SHALL scroll within itself, so neither makes the page scroll sideways.

#### Scenario: Reports listed
- **WHEN** eval reports exist
- **THEN** the screen shows each run's date, suite, mode and metrics in a table

#### Scenario: A metric over time
- **WHEN** several runs of a suite exist
- **THEN** the screen plots that suite's metric across those runs with the baseline marked

#### Scenario: A regression is named
- **WHEN** a run recorded a metric that dropped below its baseline beyond the tolerance
- **THEN** the screen names the metric, both values and the drop

#### Scenario: Too little history
- **WHEN** fewer than two runs of a suite exist
- **THEN** the screen says there is not enough history rather than drawing an empty chart

#### Scenario: Series grouped by suite and variant
- **WHEN** reports exist for retrieval (variants dense, hybrid) and selection (variant agent)
- **THEN** the picker offers the groups `selection · agent`, `retrieval · hybrid`, `retrieval · dense` in that order, each option inside naming only its metric

#### Scenario: Breakdowns follow their metric
- **WHEN** a retrieval variant reports `mrr`, `offDomainSilence`, `recall@20`, `recall@5`, `recall@5:bg`, `recall@5:bg-latn` and `recall@5:en`
- **THEN** its group lists `recall@5`, `recall@5 · en`, `recall@5 · bg`, `recall@5 · bg-latn`, `recall@20`, `mrr`, `offDomainSilence`

#### Scenario: Languages before other splits
- **WHEN** an intent variant reports `accuracy`, `accuracy:holdout`, `accuracy:bg`, `accuracy:design`, `accuracy:en` and `forcedWhenShould`
- **THEN** its group lists `accuracy`, `accuracy · en`, `accuracy · bg`, `accuracy · design`, `accuracy · holdout`, `forcedWhenShould`

#### Scenario: A suite the screen has never seen
- **WHEN** a report arrives for a suite outside the known order
- **THEN** its group is listed after every known suite's groups, and its metrics are grouped and ordered by the same rules

#### Scenario: A new metric joins its group
- **WHEN** a newer run adds a breakdown such as `recall@5:de` to a variant that already had `recall@5`
- **THEN** the new series is listed inside that variant's group right after `recall@5`'s other language breakdowns, not at the end of the picker

#### Scenario: The chosen series survives a refresh
- **WHEN** a series has been chosen and the reports are fetched again with more series
- **THEN** the same series stays chosen and its suite and variant stay visible next to the picker

#### Scenario: Phone width
- **WHEN** the screen is shown 375 px wide with reports whose metrics column is wider than that
- **THEN** the Trend card and its picker fit inside the viewport and only the runs table scrolls sideways

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

The zero-retrieval signal SHALL describe the turn, not one of its searches: it SHALL be raised when
a turn searched the documentation and finished with no sources at all. A turn that searched more
than once and ended with sources SHALL NOT carry it, whatever any single search returned along the
way. The queue is where a reviewer labels turns that went wrong, and a turn that answered from
documentation it cited has nothing to label.

#### Scenario: Zero-result turn flagged
- **WHEN** a turn searches the documentation and ends with no sources
- **THEN** the turn appears in the review queue with the signal "zero retrieval results"

#### Scenario: A turn that searched again and found something
- **WHEN** a turn's first search returns nothing and a later search returns documentation the answer cites
- **THEN** the turn does not carry the zero-retrieval signal

#### Scenario: A turn that never searched
- **WHEN** a turn answers without searching the documentation at all
- **THEN** it does not carry the zero-retrieval signal, whatever else it carries

#### Scenario: Label appended
- **WHEN** a reviewer submits a label for a flagged turn
- **THEN** a row is appended to the corresponding eval dataset

### Requirement: Two-pane chat with behind-the-scenes monitor
The `/chat` screen SHALL show the conversation in a left pane and a behind-the-scenes monitor in a right pane. On
screens narrower than 1024 px the monitor SHALL stack below the conversation. The monitor SHALL follow the turn
that is streaming, and SHALL switch to a past assistant turn when the user selects it.

The monitor SHALL be a panel the user can close and open again. Each assistant turn SHALL carry a control that
says whether the monitor is showing that turn; pressing it on the turn being shown SHALL close the monitor, and
pressing it again SHALL open the monitor on that turn. While the monitor is closed the conversation SHALL take
the room it leaves. Selecting a turn any other way SHALL only show it — nothing but that control SHALL close the
monitor, because closing it by clicking the answer being read would be a surprise.

#### Scenario: Layout
- **WHEN** a user opens `/chat` on a wide screen
- **THEN** the chat is on the left and the monitor on the right

#### Scenario: Select a past turn
- **WHEN** the user clicks an earlier assistant turn
- **THEN** the monitor loads and shows that turn's stored trace

#### Scenario: Closing the monitor
- **WHEN** the user presses the control on the turn the monitor is showing
- **THEN** the monitor closes and the turn is no longer marked as shown

#### Scenario: Opening it again
- **WHEN** the user presses that control again
- **THEN** the monitor opens on that turn and shows its trace

#### Scenario: Reading an answer does not close it
- **WHEN** the user clicks the body of the turn the monitor is showing
- **THEN** the monitor stays open

### Requirement: Monitor views
The monitor SHALL present the turn trace as:
- a timeline of all events with elapsed times and durations;
- a model-calls view showing each request (messages, tools, tool mode) and response (text, tool calls, tokens, latency);
- a retrieval view showing tenant scope, settings, query terms, and dense, sparse and fused candidates with scores and
  rerank order;
- an MCP view with raw arguments and results and the serving replica;
- a prompt and memory view with the system prompt, tool schemas and history window;
- an AG-UI view listing every event the run put on the wire.

Every event's raw data SHALL be expandable.

The retrieval view SHALL list every query term the search reported, including a term the indexed corpus has never
seen. For such a term it SHALL say that the term is outside the vocabulary and cannot match on BM25, in place of a
weight, and SHALL NOT render a weight the diagnostics did not give. The view SHALL do this for a turn in which
every term is outside the vocabulary as readily as for one in which none is.

The AG-UI view SHALL list the frames as one flat, ordered row each, never coalescing repeated types, showing the
frame's position in the run, the milliseconds since the run started, the protocol event type, the custom event's
name when it has one, and its size, with the payload expandable on the row. It SHALL include the frames the rest
of the screen makes no use of — the run starting, a text message opening and closing, a tool call ending, a custom
event under an unrecognised name, an event type the client does not handle — and SHALL show a frame whose payload
could not be read as such rather than dropping it. A frame carrying a trace event SHALL be shown by its name and
that event's sequence number, pointing at the other views rather than repeating its data. Selecting the view
SHALL be the only thing needed to see the frames; there SHALL be no separate control that starts or stops
recording them.

While a turn is streaming, the view SHALL show the frames as the client reads them. For a turn whose run has
ended, it SHALL show the frames recorded for that turn, and SHALL say that they were not recorded for a turn that
has none rather than showing an empty list.

#### Scenario: Retrieval internals visible
- **WHEN** a turn's search_documents call completes
- **THEN** the retrieval view lists the dense, sparse and fused candidates with chunk ids and scores for that call

#### Scenario: A query term the corpus has never seen
- **WHEN** a user opens the retrieval view for a turn whose question contained a term outside the indexed vocabulary
- **THEN** the view lists that term, says it cannot match on BM25 instead of showing a weight, and renders the rest of the search normally

#### Scenario: Live timeline
- **WHEN** a turn is streaming
- **THEN** new timeline entries appear as their trace events arrive

#### Scenario: Every frame has a row
- **WHEN** a turn streams an answer in twelve pieces
- **THEN** the AG-UI view shows twelve separate content rows between the text message's start and end rows

#### Scenario: A frame the rest of the screen ignores
- **WHEN** a run emits a custom event under a name the client does not recognise
- **THEN** the AG-UI view shows that frame with its name and payload, and the rest of the run still renders

#### Scenario: A trace frame points at the other views
- **WHEN** the AG-UI view lists a frame carrying a trace event
- **THEN** the row names the trace event and its sequence number instead of repeating its data

#### Scenario: A stored turn's frames
- **WHEN** a user opens an earlier assistant turn whose frames were recorded
- **THEN** the AG-UI view lists them in the order the run wrote them

#### Scenario: A turn with no recorded frames
- **WHEN** a user opens a turn for which no frames were recorded
- **THEN** the AG-UI view says so rather than showing an empty list

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

The AG-UI view SHALL follow the same cursor: the frames up to and including the one that carried the cursor's
trace event SHALL be shown as reached, that frame SHALL be highlighted, and every later frame SHALL be dimmed.
With the cursor before the first step, no frame SHALL be shown as reached.

#### Scenario: Retrieval before and after
- **WHEN** the cursor is before the `retrieval` event
- **THEN** the Retrieval tab shows no candidates, and moving the cursor onto that event shows its dense, sparse and fused lists

#### Scenario: Model call in progress
- **WHEN** the cursor is on a `model.request` whose `model.response` comes later
- **THEN** the Model tab shows the request with a "waiting for response" state

#### Scenario: Frames as of a step
- **WHEN** the cursor is on the trace event of a turn's `tool.call`
- **THEN** the AG-UI view highlights the frame that carried that trace event and dims every frame after it

#### Scenario: Before the first step
- **WHEN** the cursor is before the first step
- **THEN** the AG-UI view dims every frame and highlights none

### Requirement: The retrieval view shows what the floor did
The retrieval view SHALL state the relevance floor applied to each candidate branch, alongside the other search
settings it already shows, so that an operator reading a search knows which floor produced it.

Where diagnostics report candidates that fell below a floor, the view SHALL show them, marked as dropped and
visibly apart from the candidates that were returned. It SHALL NOT hide them: a search that found near misses and
a search that found nothing at all look identical once the near misses are gone, and telling those two apart is
the reason to open this view.

A search that returned nothing SHALL say so, and say whether anything was found and dropped, rather than showing
empty lists.

#### Scenario: The floors are stated
- **WHEN** a user opens the retrieval view for any search
- **THEN** the view shows the floor applied to each branch

#### Scenario: Near misses are visible
- **WHEN** a search returned nothing because every candidate fell below its floor
- **THEN** the view lists those candidates with their scores, marked as dropped, and says the search returned nothing

#### Scenario: Nothing was found at all
- **WHEN** a search found no candidates before the floor was applied
- **THEN** the view says so, distinctly from a search whose candidates were all dropped

#### Scenario: Dropped candidates are not mistaken for results
- **WHEN** a search returned some results and dropped others
- **THEN** the returned and the dropped candidates are told apart on the screen

### Requirement: A failing monitor view stays inside that view
A defect while rendering one monitor view SHALL NOT remove the screen around it. The conversation, its answers and
the controls for sending the next message SHALL remain on screen and usable, and the monitor's other views SHALL
remain selectable and SHALL render.

In place of the failing view the monitor SHALL say that this view could not be shown and name the view. It SHALL
NOT put anything internal on the page — no exception type, message, stack frame, hostname or query text — and that
SHALL be asserted against what is rendered. The failure SHALL be recoverable without reloading the page: selecting
another view and returning SHALL attempt the view again.

#### Scenario: One view fails
- **WHEN** rendering one monitor view throws
- **THEN** that view is replaced by a message naming it, and the chat around the monitor stays on screen and usable

#### Scenario: The other views are unaffected
- **WHEN** one monitor view has failed
- **THEN** the remaining views can still be selected and render their content

#### Scenario: Nothing internal is rendered
- **WHEN** a monitor view has failed
- **THEN** the rendered output contains no exception type, message, stack frame, hostname or query text

#### Scenario: Recovering without a reload
- **WHEN** a user leaves the failed view and comes back to it
- **THEN** the view is rendered again rather than staying failed for the rest of the session

### Requirement: Chat as of a step
When the user has moved the cursor of the selected turn to before its last step, the chat pane SHALL show that turn as
it was at the cursor:
- the answer text reconstructed from the `answer.delta` events up to the cursor;
- the reasoning reconstructed from the `reasoning.delta` events up to the cursor, open while the cursor is still
  among them and closed once the answer has started;
- tool cards in their running or finished state;
- sources only once the `sources` event is reached;
- a banner "viewing step k of N" with a control to return to the present.

A cursor that follows the newest step SHALL never show the turn as of a step, not even for a single frame while new
events arrive. The banner SHALL appear only after the user moves the cursor: with the scrubber, a step, a jump, or
playback.

Other turns in the chat SHALL be unaffected.

#### Scenario: Rewind the answer
- **WHEN** the cursor is on the first `answer.delta` event of a turn
- **THEN** the chat shows only that chunk of the answer for that turn, with the time-travel banner

#### Scenario: Rewind the reasoning
- **WHEN** the cursor is on the first `reasoning.delta` event of a turn
- **THEN** the chat shows only what the model had reasoned by then, in an open block, and no answer text

#### Scenario: Tool card rewinds
- **WHEN** the cursor is between a `tool.call` and its `tool.result`
- **THEN** the chat shows that tool card in the running state

#### Scenario: A streaming turn is not rewound
- **WHEN** a turn streams its reasoning with the monitor open and the user has not moved the cursor
- **THEN** no render of the chat shows the time-travel banner for that turn

#### Scenario: Another turn starts at its newest step
- **WHEN** the user has moved the cursor on one turn and then selects another
- **THEN** the other turn is shown as it is now, without the banner

### Requirement: Topology screen
The `/topology` screen SHALL render the drawn diagram of the stack with its live state on top. Each node SHALL show
its health (healthy, degraded, unreachable) in a way that does not rely on colour alone, its replica instances, and
the facts reported for it; each edge SHALL be drawn as the diagram draws it. Edge labels SHALL be legible: no label SHALL overlap another
label or a node, and a label drawn over a line SHALL stay readable against it. The screen SHALL be reachable from the
main navigation and open to any signed-in user.

The screen SHALL refresh the state while it is open, SHALL say when the state was last refreshed, and SHALL offer a
manual refresh. Selecting a node SHALL show its full reported detail. When the state cannot be loaded, the diagram
SHALL still be shown, with an error telling the user the state is unknown rather than an empty screen.

#### Scenario: Live state on the diagram
- **WHEN** a signed-in user opens `/topology` with the stack running
- **THEN** every node is drawn in its place and marked healthy, and api and the MCP server show their replicas

#### Scenario: Labels stay apart
- **WHEN** two edges' labels would sit in the same place, for example where two lines cross
- **THEN** each label is moved along its own line until it overlaps no other label and no node

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

### Requirement: An error shows the face it deserves
The chat screen SHALL tell three kinds of failure apart. Something that was retried and recovered SHALL say
little or nothing. Something broken SHALL say what is unavailable and what to do about it. Something refused
SHALL say only that it was refused, without saying why. No text from an internal error — an exception type, a
stack frame, a hostname, a query — SHALL reach the page, and that SHALL be asserted against what is rendered.

#### Scenario: Recovered
- **WHEN** a turn succeeds after a retry
- **THEN** the answer is shown and nothing suggests a failure

#### Scenario: Unavailable
- **WHEN** the assistant cannot be reached
- **THEN** the screen says it is unavailable and what to try, and offers to send the message again

#### Scenario: Refused
- **WHEN** the conversation belongs to someone else
- **THEN** the screen says only that it is not available

#### Scenario: Nothing internal is rendered
- **WHEN** any of these failures is rendered
- **THEN** the rendered output contains no exception type, stack frame, hostname or query text

### Requirement: A failed turn stays on screen with its error
When a run ends in an error, the chat screen SHALL keep the conversation it is showing and the turn as it was streamed,
and SHALL show the run's error under that turn. A run's end that names no conversation SHALL NOT change which
conversation is on screen, and SHALL NOT cause the conversation to be reloaded or its turns to be replaced. The error
shown SHALL follow "An error shows the face it deserves": no internal text reaches the page.

#### Scenario: First turn of a new conversation fails
- **WHEN** a person sends the first message of a new conversation and the run ends in an error
- **THEN** their question stays on screen with the error shown under it, and the conversation is not reloaded

#### Scenario: A later turn fails
- **WHEN** a person sends a message in a conversation that already has answers and the run ends in an error
- **THEN** the earlier turns and the new question stay on screen, the error is shown under the new question, and the
  conversation on screen is still the same one

#### Scenario: The next message continues the same conversation
- **WHEN** a turn has failed and the person sends another message
- **THEN** the message is sent in the same conversation, not a new one

### Requirement: A2A screen
A FIRM_ADMIN SHALL have a screen showing what arrived from partner systems, what this system asked of another
agent, and every push delivery — each with its state and timing — and SHALL be able to cancel a task of their
firm that is still running. A person who is not a FIRM_ADMIN SHALL NOT reach it.

The screen SHALL also have a read-only section for the test-generation agent, loaded from the api on its own and
shown whether or not any partner has talked to the system: whether the agent is reachable (or not configured), its
card (name, version, description, skill, endpoint, required scope, the partner id the api signs in as), the run
defaults and limits (default model, attempts, tool rounds, test runs, suspected bugs, deadline, no default budget),
the counts of runs by group with a link to the Coverage page, and the most recent runs, each file opening on the
Coverage page. It SHALL use the screen's existing design and theme tokens in both themes.

Each recent run SHALL show how long it took, as the api reports it, in a compact form — seconds under a minute
(`42s`), minutes and two-digit seconds under an hour (`3m 05s`), hours and two-digit minutes beyond (`1h 02m`) — and
`—` when the api reports none. A running run's duration SHALL be marked as running so far and SHALL keep counting on
the page, at least once a second, from the api's value plus the time since that answer arrived, until the next
refresh replaces it.

While the screen's data loads or is refreshed it SHALL show the app's themed progress indicator
(`role="progressbar"` with an accessible label), and the Refresh button SHALL NOT start a second refresh while one is
in flight. A section that fails to load SHALL say what could not be loaded without hiding the other.

#### Scenario: What is there
- **WHEN** a FIRM_ADMIN opens the A2A screen
- **THEN** inbound tasks, outbound consultations and push deliveries are listed, each with its state

#### Scenario: Cancelling from the screen
- **WHEN** the admin cancels a running task
- **THEN** the task is reported cancelled and the list shows it

#### Scenario: Not an admin
- **WHEN** an advisor tries to open it
- **THEN** the screen is not available to them

#### Scenario: Nothing yet
- **WHEN** no agent has talked to this system
- **THEN** the screen says so rather than showing empty tables, and the test-generation agent section is still shown

#### Scenario: The test agent section
- **WHEN** a FIRM_ADMIN opens the screen and the test agent is reachable
- **THEN** the section shows it reachable, its card, its run defaults, the run counts with a link to Coverage, and
  the recent runs with file, state, attempt n/N, coverage, reason, duration and when

#### Scenario: Durations in the recent runs
- **WHEN** the api reports a finished run that took 185 000 ms, a running run at 42 000 ms, and a run with no
  duration
- **THEN** the first reads `3m 05s`, the second reads `42s` marked as so far and a second later `43s`, and the third
  reads `—`

#### Scenario: The test agent is down
- **WHEN** the api reports the test agent unreachable
- **THEN** the section says so with the reason, and still shows the run defaults and runs

#### Scenario: Loading
- **WHEN** the screen is opened and the api has not answered yet
- **THEN** a themed progress indicator with an accessible label is shown in place of each section until it answers

#### Scenario: The overview fails
- **WHEN** the test agent overview cannot be loaded
- **THEN** the section says so, and the partner activity is still shown

### Requirement: The model's reasoning in the chat
When a turn's model reasons before it answers, the assistant's turn SHALL show that reasoning in the chat, set
apart from the answer and marked as the model's thinking rather than as something said to the user.

While the model is reasoning the block SHALL be open and SHALL grow as the reasoning streams. It SHALL close by
itself when the answer's first text arrives, leaving a summary that says how long the model thought and that
reopens the reasoning when it is used. Once closed by the answer, it SHALL NOT reopen by itself, and a person who
opened or closed it SHALL keep that choice for the rest of the turn.

A turn whose model did not reason SHALL show no such block. A turn being restored from history SHALL show its
reasoning when the stored trace it came from is loaded, and SHALL show none when that trace has expired.

#### Scenario: Reasoning while the model thinks
- **WHEN** a turn's model streams its reasoning
- **THEN** the assistant's turn shows an open block that grows with it, and the answer has not started

#### Scenario: The answer closes it
- **WHEN** the first text of the answer arrives
- **THEN** the block closes itself and shows how long the model thought, and opening it shows the reasoning again

#### Scenario: A person's choice is kept
- **WHEN** a person opens the block after it closed itself
- **THEN** it stays open for the rest of the turn

#### Scenario: A turn with no reasoning
- **WHEN** a model answers without reasoning
- **THEN** the turn shows no reasoning block

#### Scenario: A reopened turn
- **WHEN** a user opens a stored turn whose trace holds its reasoning
- **THEN** that turn shows the reasoning, collapsed, with how long the model thought

### Requirement: Telemetry screen
The `/telemetry` screen SHALL show what the stack has measured about itself, over a period the user chooses, and
SHALL be reachable from the main navigation.

It SHALL show: how many runs and turns were started and how many failed; how long a turn takes; how long a model
call takes and how many tokens it used, by model; tool calls by tool and outcome; how long retrieval takes, split
into its stages; and how those numbers are spread across the instances that served them. Each number SHALL say
which period it covers, and a number the stack has not measured yet SHALL read as no data rather than as zero.

The screen SHALL offer a way to open a turn's trace where the spans are kept, and a turn on the chat screen SHALL
offer the same for its own trace. When the metrics cannot be read, the screen SHALL say so and stay usable rather
than showing an empty chart.

No message content SHALL appear on the screen, because none of it is in the signals it reads.

#### Scenario: The stack's own numbers
- **WHEN** a signed-in user opens `/telemetry` after turns have run
- **THEN** the screen shows the turn and model numbers for the chosen period, each labelled with that period

#### Scenario: Nothing measured yet
- **WHEN** no turn has run in the chosen period
- **THEN** the screen says there is no data for it rather than showing zeros as a result

#### Scenario: Per instance
- **WHEN** two api replicas have served turns
- **THEN** the screen shows how the turns were spread between them

#### Scenario: From a turn to its trace
- **WHEN** a user opens the trace of a turn from the chat screen
- **THEN** that turn's spans open where the traces are kept

#### Scenario: Metrics unavailable
- **WHEN** the metrics store cannot be reached
- **THEN** the screen says the numbers are unavailable and still renders

### Requirement: Jev statistics screen
The `/admin/jev` screen SHALL show, for a period the user chooses, the statistics of every Jev call site in chat turns
as charts. It SHALL say that Jev calls made on the A2A partner path are not counted there.

The screen SHALL be reachable from the main navigation, and `/admin/intents` SHALL continue to reach it. Like the other
admin screens, it SHALL be shown only to FIRM_ADMIN.

It SHALL show a cross-cutting overview and a section per call site:

- **Overview:**
  - the total Jev requests over the period;
  - how many requests were unavailable;
  - the requests per classified turn;
  - total requests and unavailability over time, so a degraded Jev is visible across every site at once.
- **Intent:**
  - headline numbers: classified turns, share used, gated and failed, forced-retrieval rate, p90 latency;
  - a diagram of the classification pipeline, with the configured floors and how many turns took each path;
  - turns over time by outcome;
  - timeouts over time;
  - Jev's choice against the intent the turn proceeded with;
  - the reasons an answer was not used;
  - the confidence histogram with the confidence floor marked;
  - the in-domain histogram with the in-domain floor marked;
  - a confidence × in-domain scatter by outcome, with both floors marked;
  - the mean probability per intent;
  - the latency histogram with the timeout marked, and the latency percentiles;
  - the model versions that answered;
  - the history of the `intent` eval: accuracy per language and per split, forcing rates, and the latest run's
    failures.
- **Guardrail:**
  - how many prompts, tool results and reviewer texts were screened, and their decisions;
  - the question that tripped a block or a withholding;
  - the blocked, withheld and unscreened counts;
  - screenings over time, with the unscreened ones marked.
- **Relevance & rerank:**
  - how many searches Jev judged;
  - how many the gate silenced;
  - how many were reranked by Jev;
  - how many were left ungated because Jev was unavailable;
  - the distribution of each search's top relevance against the floor;
  - the judge latency.
- **Routing:**
  - how many data turns there were;
  - how many were routed, and to which tool;
  - why a data turn was not routed;
  - the model calls a turn made, routed against unrouted.

Each chart SHALL:
- name its period;
- identify series by a legend or labels, not by colour alone;
- offer the values behind the marks on hover;
- read as no data, not as zero, when nothing was recorded.

When the statistics cannot be loaded, the screen SHALL say so and stay usable. No message content SHALL appear on the
screen.

#### Scenario: Charts after classified turns
- **WHEN** a FIRM_ADMIN opens `/admin/jev` after turns were classified
- **THEN** the headline numbers, the pipeline diagram and every chart are shown for the chosen period, with the floors
  drawn at 0.5, 0.2 and the timeout

#### Scenario: Nothing classified in the period
- **WHEN** no turn was classified in the chosen period
- **THEN** the screen says there is no data for it instead of drawing empty charts

#### Scenario: Statistics unavailable
- **WHEN** the statistics request fails
- **THEN** the screen shows an error and still renders its controls

#### Scenario: Intent eval history
- **WHEN** intent eval reports exist
- **THEN** the screen draws accuracy per language and per split over the runs, and lists the latest run's failures

#### Scenario: Not an admin
- **WHEN** an ADVISOR opens `/admin/jev`
- **THEN** the screen shows access denied, as the other admin screens do

#### Scenario: Every Jev call site has a section
- **WHEN** a FIRM_ADMIN opens `/admin/jev` after turns that screened prompts and tool results, gated and reranked
  searches, and routed data turns
- **THEN** the overview and the guardrail, relevance-and-rerank and routing sections are shown alongside the intent
  section, each with its own charts for the chosen period

#### Scenario: The scope is stated
- **WHEN** a FIRM_ADMIN opens `/admin/jev`
- **THEN** the screen says that it counts Jev calls made by chat turns, and that the A2A partner path is not counted

### Requirement: The retrieval view shows Jev's relevance judgment
For each search in a turn that asked Jev for a relevance judgment, the monitor's retrieval view SHALL show:
- Jev's model;
- the floor;
- the highest probability;
- whether the gate kept or silenced the search, or the reason Jev did not answer;
- whether Jev's answer ordered the results;
- how long the judgment took.

When the retrieval diagnostics carry each judged candidate's probability, the view SHALL list them by chunk. When
diagnostics were not requested, the view SHALL still show the judgment from the turn's `relevance` event.

The timeline SHALL show each `relevance` event as its own row with its duration.

#### Scenario: A judged search in the retrieval view
- **WHEN** a turn's search was judged by Jev and silenced by the gate
- **THEN** the retrieval view shows the model, the floor, the highest probability, "silenced", and each judged
  candidate's probability

#### Scenario: Judgment without diagnostics
- **WHEN** retrieval diagnostics were turned off and a search was judged
- **THEN** the retrieval view shows that search's judgment from its `relevance` event, and the timeline shows the
  `relevance` row with the judge's duration

### Requirement: Domains view in the monitor
The monitor SHALL have a Domains view showing:
- Jev's probability for each domain against the scope floor;
- the domains in scope, and whether the question crosses the boundary;
- the path the turn took: each tool call in order, with its domain, its server instance and its latency;
- each boundary crossing between calls;
- where the prediction and the calls disagree.

When a turn touched more than one domain, the monitor header SHALL show the path as a chip (e.g. `billing → portfolio`).

#### Scenario: Crossing turn in the monitor
- **WHEN** a person opens the Domains view of a turn that crossed from billing to portfolio
- **THEN** they see both domains' probabilities, the calls grouped by domain in order, and the crossing between them

### Requirement: Curriculum map screen
The `/curriculum` screen SHALL explain where the concepts and rules of the 5-day Fullstack AI Engineer study plan
(revision 7) are applied in the lab. It SHALL be reachable from the main navigation.

The screen SHALL group its entries by the plan's days, in the plan's order, followed by a section for the plan's
rules. Each entry SHALL give:
- the concept's name;
- a short explanation, of two or three sentences, of how the lab applies it;
- the repository paths that implement it;
- the spec that states it;
- a link to the screen where it can be seen, when such a screen exists.

The screen SHALL end with a section naming the plan's topics the lab does not implement, each with its reason, so the
page never claims coverage the code does not have.

The screen SHALL render from content shipped with the web app. It SHALL NOT call the api and SHALL NOT require a
session or a dev persona.

#### Scenario: Entries grouped by day
- **WHEN** the user opens `/curriculum`
- **THEN** one labelled section per day of the plan appears in the plan's order, then the rules section, then the not-covered section

#### Scenario: An entry names where to look
- **WHEN** the user reads an entry
- **THEN** it shows the explanation, at least one repository path and the spec name
- **AND** when the concept is visible in the app, a link opens that screen

#### Scenario: Readable without a persona
- **WHEN** no dev persona is picked
- **THEN** the whole page renders and no api request is made

#### Scenario: Honest about gaps
- **WHEN** the user reaches the end of the page
- **THEN** each topic of the plan that the lab does not implement is listed with its reason

### Requirement: The monitor shows Jev's answer check
The monitor's timeline SHALL show a turn's `answer.check` event as a row of its own, in a colour of its own kind, with a
bar whose length is the check's latency and a title that names Jev, both probabilities against their floors and the
verdict (for example "Jev answer check: relevant 0.93 ≥ 0.50, grounded 0.41 < 0.50 — not grounded", or "Jev answer
check unavailable: <reason> — unchecked"). The event's data SHALL be readable in the row's details; no view of its own
is required. When the verdict is `not_relevant` or `not_grounded`, the monitor's header SHALL name it ("answer: not
grounded"); a pass or an unchecked answer SHALL add nothing to the header.

#### Scenario: A flagged answer in the monitor
- **WHEN** a turn whose answer Jev found not grounded is shown
- **THEN** the timeline has an `answer.check` row with its own colour and a bar for its latency, and the header says "answer: not grounded"

#### Scenario: A passing answer
- **WHEN** a turn whose answer passed the check is shown
- **THEN** the timeline has the `answer.check` row and the header has no answer chip

### Requirement: The Jev statistics screen shows the answer check
The `/admin/jev` screen SHALL list `answer` among the call sites of its overview with its requests, unavailable
requests and latency percentiles, and SHALL show an Answer check section: answers checked, the share not relevant and
not grounded with the floors, unchecked answers and how many of those Jev was unavailable for, and a latency histogram
against the check's timeout. A window with no answer check SHALL say so instead of drawing empty charts.

#### Scenario: Answer-check numbers on the screen
- **WHEN** the statistics report nine answer-check requests, one unavailable, and eight checked answers of which two were not grounded
- **THEN** the overview's `answer` row shows 9 and 1, and the Answer check section shows 25% not grounded and the latency histogram

#### Scenario: Nothing checked
- **WHEN** the window holds no answer check
- **THEN** the Answer check section says that no answer was checked

### Requirement: The Jev statistics screen shows skipped calls
The `/admin/jev` overview SHALL show how many calls an open circuit skipped over the period. It SHALL show the count
per site, next to that site's unavailable requests.

The requests and unavailability timeline SHALL draw the skipped calls as a series of their own. The series SHALL be named
in the legend, and its values SHALL be offered on hover. An outage therefore stays visible after the breaker opens.

When no call was skipped in the period, the skipped count SHALL read 0 and the timeline SHALL draw no skipped series.

#### Scenario: Outage with an open circuit
- **WHEN** a FIRM_ADMIN opens `/admin/jev` for a period in which Jev timed out and the circuit then skipped calls
- **THEN** the overview shows the unavailable requests and the skipped calls as separate numbers per site, and the
  timeline shows both series in the buckets where they occurred

#### Scenario: No skipped calls
- **WHEN** no call was skipped in the chosen period
- **THEN** the skipped count reads 0, and the timeline shows only requests and unavailability

### Requirement: Earlier prompts recalled with the arrow keys
The chat input SHALL let the user recall the prompts they sent earlier in the conversation on screen.

**The history:**
- The history is the user's prompts shown in that conversation, oldest to newest. This includes prompts restored from a
  stored conversation.
- The history SHALL be read from the page's own state. Recalling SHALL send nothing and store nothing.

**ArrowUp:**
- Pressing ArrowUp SHALL replace the input's text with the previous prompt in the history. Each further ArrowUp SHALL
  step one prompt further back.
- At the oldest prompt, ArrowUp SHALL leave the input unchanged.

**ArrowDown:**
- ArrowDown SHALL step one prompt forward.
- Stepping forward past the newest prompt SHALL restore the text the input held before the recall began, which may be
  empty.

**When recall takes over the key:**
- ArrowUp SHALL recall only when the caret is on the input's first line and no text is selected.
- ArrowDown SHALL step forward only during a recall, with the caret on the input's last line and no text selected.
- A key pressed with Shift, Ctrl, Alt or Meta held, or while an input method is composing, SHALL NOT recall.
- In every other case the arrow keys SHALL move the caret as they normally do.

**After a recall:**
- The caret SHALL be placed at the end of the recalled text.

**When a recall ends:**
- Editing the input's text SHALL end the recall. The edited text becomes the draft, and the next ArrowUp starts again
  from the newest prompt.
- Sending a message, starting a new conversation or opening another conversation SHALL end any recall.

#### Scenario: Stepping back through earlier prompts
- **WHEN** the user has sent "first", "second" and "third" in the conversation, and presses ArrowUp in the empty input
  three times
- **THEN** the input shows "third", then "second", then "first"

#### Scenario: The oldest prompt is the limit
- **WHEN** the input shows the oldest prompt and the user presses ArrowUp
- **THEN** the input still shows the oldest prompt

#### Scenario: Stepping forward restores the draft
- **WHEN** the user has typed "half a question", pressed ArrowUp twice, then presses ArrowDown twice
- **THEN** the input shows the newer prompt and then "half a question" again

#### Scenario: No prompts yet
- **WHEN** the conversation has no user prompt and the user presses ArrowUp
- **THEN** the input is unchanged

#### Scenario: Caret movement inside a multi-line draft
- **WHEN** the input holds two lines and the caret is on the second line, and the user presses ArrowUp
- **THEN** the caret moves to the first line and no prompt is recalled

#### Scenario: A stored conversation's prompts
- **WHEN** the user opens a stored conversation whose last question was "status of run 4417" and presses ArrowUp
- **THEN** the input shows "status of run 4417"

#### Scenario: Editing ends the recall
- **WHEN** the user recalls "second", changes it to "second, for firm B", and presses ArrowUp
- **THEN** the input shows the newest prompt, and ArrowDown past it restores "second, for firm B"

#### Scenario: Sending ends the recall
- **WHEN** the user recalls a prompt, sends it, and presses ArrowUp in the empty input
- **THEN** the input shows the prompt just sent

#### Scenario: A modifier key does not recall
- **WHEN** the user presses Shift+ArrowUp in the empty input
- **THEN** no prompt is recalled

### Requirement: Data cards in the chat
The chat SHALL render each data card of a turn as a table in that turn, where the card arrived. A card that arrives
before the answer text SHALL be shown at once. The card SHALL NOT wait for the answer.

**The three card types:**
- **Holdings** (`maf-lab/holdings`) SHALL show:
  - one row per asset class: market value, target weight, actual weight, and drift with the tolerance band drawn;
  - the trade to target, labelled buy or sell in words, and the weight after;
  - a total row;
  - a badge saying whether a rebalance is needed.
  - A class outside the tolerance SHALL be marked by text or an icon as well as by colour.
- **AUM history** (`maf-lab/aum-history`) SHALL show one row per quarter end, with its AUM and its change from the
  previous quarter.
- **Accounts** (`maf-lab/accounts`) SHALL show one row per account: its id, name, household, model portfolio and
  currency.

**Every card SHALL:**
- be a table with a caption naming the account, or the user's accounts, and the as-of date where there is one;
- align numbers right, in tabular figures;
- format amounts and percentages for the language of the turn's question: Bulgarian when it is written in Cyrillic,
  English otherwise. Amounts use the card's currency.
- offer to copy its rows as CSV;
- scroll horizontally inside itself at phone width, without scrolling the page;
- read correctly in both light and dark themes.

**Other rules:**
- A card of an unknown type SHALL be ignored.
- When a turn is replayed to a step (time travel), only the cards that had arrived by that step SHALL be shown.

#### Scenario: A holdings card before the answer
- **WHEN** the portfolio tool for A-1043 returns while the model has not yet written anything
- **THEN** the holdings table appears in the turn with four classes, their trades and a "no rebalance needed" badge,
  before any answer text

#### Scenario: Bulgarian formatting
- **WHEN** the question was "Препоръчай ребалансиране за A-1043"
- **THEN** amounts read like "268 000 $" and percentages like "20,6 %"

#### Scenario: Buy and sell are words, not only colours
- **WHEN** the plan sells US equity and buys cash
- **THEN** the rows say "Продажба" and "Покупка", or "Sell" and "Buy" in English, next to the amounts

#### Scenario: Copy as CSV
- **WHEN** the user presses "Copy as CSV" on a card
- **THEN** the clipboard receives a header row and one row per table row, with plain numbers

#### Scenario: An unknown card type
- **WHEN** an activity of type `maf-lab/something-new` arrives
- **THEN** nothing is rendered for it and the turn still completes

#### Scenario: Time travel before the card
- **WHEN** a turn is replayed to a step before its portfolio tool returned
- **THEN** the holdings card is not shown, and it appears at the step where it arrived

### Requirement: The answer is rendered as safe markdown
The chat SHALL render the assistant's answer as GitHub-flavoured markdown. This covers:
- paragraphs, emphasis and strong emphasis;
- ordered and unordered lists;
- inline code and code blocks;
- tables;
- links.

It SHALL do so while the answer streams, when a conversation is reopened, and when a turn is shown at an earlier step
in time travel. The user's messages and the reasoning block SHALL stay plain text.

**Nothing in an answer SHALL be able to run script, load a resource or navigate the page on its own:**
- Raw HTML SHALL NOT be interpreted; it SHALL be shown as text.
- A link SHALL be rendered only for the `http`, `https` and `mailto` schemes. It SHALL open in a new tab with
  `rel="noopener noreferrer"`.
- A link with any other scheme SHALL be rendered as plain text.
- An image SHALL NOT be loaded; its alt text SHALL be shown instead.

**Tables** SHALL align numbers to the right in tabular figures, and SHALL scroll inside the answer at phone width
without scrolling the page.

#### Scenario: A procedure with steps
- **WHEN** the answer contains `1. Open the failed run\n2. Assign the schedule` and `**FS-REQUIRED**`
- **THEN** the steps render as an ordered list and FS-REQUIRED renders in bold, with no `**` or `1.` visible as text

#### Scenario: A table in an answer
- **WHEN** the answer contains a GFM table
- **THEN** it renders as a table with a header row

#### Scenario: HTML in an answer
- **WHEN** the answer contains `<img src=x onerror=alert(1)>` or `<script>`
- **THEN** no element is created from it and the text is shown literally

#### Scenario: An unsafe link
- **WHEN** the answer contains `[click](javascript:alert(1))`
- **THEN** "click" is shown as plain text and no link is created

#### Scenario: A safe link
- **WHEN** the answer contains `[docs](https://example.com/x)`
- **THEN** it renders as a link that opens in a new tab with `rel="noopener noreferrer"`

#### Scenario: A half-streamed answer
- **WHEN** the answer so far ends in the middle of a list or table
- **THEN** what has arrived renders without error, and the rest completes as it streams

### Requirement: The account in focus is shown and can be changed
**The chip.**
- When the conversation has an account in focus, the chat SHALL show it in a chip above the message box.
- The chip SHALL say it is the account in focus, in the language of the page's last question.
- The chip SHALL offer ✕ to clear the focus.
- Without a focus there SHALL be no chip.

**Choosing an account.** Each row of an accounts card, and each holdings or AUM card, SHALL offer "Focus". It sets that
account as the one in focus.

**Sending the state.**
- A change made in the UI SHALL be sent with the next message as the run's state. It SHALL NOT start a run of its own.
- The chip SHALL follow the state the server sends back, so a focus the server refused does not stay on screen.

**Restoring.** Reopening a conversation SHALL restore its account in focus.

#### Scenario: Focus from a read
- **WHEN** a turn reads A-1043's portfolio
- **THEN** the chip shows A-1043 once the turn's state arrives

#### Scenario: Focus from an accounts card
- **WHEN** the user presses "Focus" on A-1044 in an accounts card and then asks "What does it hold?"
- **THEN** the request carries `state: { focus: { accountId: "A-1044" } }`, and the chip shows A-1044

#### Scenario: Clearing
- **WHEN** the user presses ✕ on the chip and sends a message
- **THEN** the request carries `state: { focus: null }`, and no chip is shown

#### Scenario: A refused focus
- **WHEN** the server's starting snapshot names a different account than the one the client sent
- **THEN** the chip shows the server's account

### Requirement: The user can choose the theme
The header SHALL offer a theme button with three modes:
- **System**, the default, which follows the operating system's light or dark setting, including a change made while
  the app is open;
- **Light**;
- **Dark**.

Each press SHALL move to the next mode, in the order System, Light, Dark, then System again. Presses that come faster
than the page redraws SHALL each count. The button SHALL say
which mode is on, in text and in its accessible name, not only with an icon.

**Remembering the choice.**
- The choice SHALL be remembered in the browser and applied on the next visit before the first paint, so the other
  theme is never shown on load.
- Choosing System SHALL forget the stored choice.
- When the browser does not allow storage, the choice SHALL still apply to the open page.
- The choice SHALL NOT be sent to the server.

**What it changes.** Every screen SHALL use the chosen theme. This includes native controls and the time-travel
banner.

#### Scenario: Forcing dark on a light system
- **WHEN** the operating system is light and the user presses the theme button until it says Dark
- **THEN** every screen is dark, and after a reload it is still dark, with no light frame first

#### Scenario: Back to the system
- **WHEN** the user presses the button until it says System
- **THEN** the app follows the operating system again, and no choice is stored

#### Scenario: The system changes while open
- **WHEN** the mode is System and the operating system switches from light to dark
- **THEN** the app turns dark without a reload

#### Scenario: Storage blocked
- **WHEN** the browser refuses storage and the user chooses Light
- **THEN** the page is light, and nothing fails

#### Scenario: A fast double press
- **WHEN** the mode is System and the user presses the button twice before the page redraws
- **THEN** the mode is Dark, not Light

### Requirement: Every colour follows the theme
Every colour the app draws SHALL come from the active theme, in the light theme and in the dark theme alike. This
covers text, borders, fills, chart and timeline marks, badges and status colours. The one exception is a translucent
scrim behind a dialog or drawer.

**Contrast.** In each theme:
- text SHALL have a contrast of at least 4.5:1 with what it sits on;
- a mark that carries meaning (a timeline bar, a status outline, a category badge) SHALL have at least 3:1 with the
  surface it sits on;
- text on a filled badge or button SHALL have at least 4.5:1 with the fill.

**Categories.** A category colour (a trace kind, an AG-UI frame family, a domain, a service state) SHALL keep the same
hue in both themes, so the same thing reads as the same colour after a switch.

#### Scenario: Timeline in the dark theme
- **WHEN** the monitor shows a turn's timeline in the dark theme
- **THEN** every kind's bar and badge meets the contrast above, and each kind has the same hue as in the light theme

#### Scenario: A degraded service
- **WHEN** Topology shows a degraded service in the light theme
- **THEN** its state text has at least 4.5:1 contrast with the surface

#### Scenario: Approving in the dark theme
- **WHEN** a confirmation card is shown in the dark theme
- **THEN** the Approve button's label has at least 4.5:1 contrast with its fill

### Requirement: The right pane has Behind the scenes and Code snippets tabs
The `/chat` screen's right pane SHALL offer two tabs.
- **Behind the scenes** SHALL be selected when the page opens and SHALL show the monitor exactly as before.
- **Code snippets** SHALL show code for the turn the pane follows: the streaming turn, or the past turn the user
  selected.
  - When that turn searched the codebase, the tab SHALL show the snippets the answer used, labelled as used by the
    answer. It SHALL NOT make a request of its own for them.
  - Otherwise it SHALL show the repository files and lines that match the turn's question, labelled as related code
    that the answer did not use. These SHALL be fetched only while the tab is shown, and again when the question
    changes.
- The tab's label SHALL show how many snippets the answer used, when it used any.
- When a streaming turn's codebase search returns snippets, the pane SHALL switch to Code snippets by itself, once per
  turn. After that the user's choice of tab stands.
- Snippets SHALL be grouped per file. Each group SHALL show the file path, and each snippet its line range, symbol and
  code with its line numbers.
- While fetching, the tab SHALL say it is searching.
- When nothing matches, it SHALL say that no code matches the question.
- When the code search is unavailable, it SHALL say so, and the Behind the scenes tab SHALL be unaffected.
- Closing and opening the pane SHALL keep the rule that only the turn's own control closes it.

#### Scenario: Default tab
- **WHEN** a user opens `/chat`
- **THEN** the right pane shows the Behind the scenes tab with the monitor

#### Scenario: Code for the question
- **WHEN** the user asks "where is the tenant filter built?" and opens Code snippets
- **THEN** the tab lists `src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs` with the matching lines, numbered

#### Scenario: The answer's own snippets
- **WHEN** the user asks "how does the code make a tool call idempotent?" and the turn searches the codebase
- **THEN** the pane switches to Code snippets, labelled with the count, showing the snippets the answer used, and no snippets request is made

#### Scenario: Related code for a turn that did not search code
- **WHEN** the user opens Code snippets on a billing turn
- **THEN** the tab fetches and shows related code, labelled as not used by the answer

#### Scenario: Selecting another turn
- **WHEN** the Code snippets tab is open and the user selects an earlier turn
- **THEN** the tab shows that turn's snippets

#### Scenario: Code search down
- **WHEN** the code snippets endpoint fails
- **THEN** the Code snippets tab shows that code search is unavailable, and switching back shows the monitor unchanged

### Requirement: Code sources open the Code snippets tab
In an answer's Sources, a code source SHALL read as its file name and line range, with its folder and symbol beneath.
No source SHALL widen the answer: a line too long for it SHALL be cut with an ellipsis and keep its full place as a
tooltip. An answer with more than five sources SHALL show five and offer the rest. Choosing it SHALL open the Code
snippets tab on that snippet and highlight it. The tool card of `search_codebase` SHALL read "Searching the
codebase…" while it runs and "Searched the codebase" when done. The Domains view SHALL show the codebase like the
other domains.

#### Scenario: A long symbol stays inside the answer
- **WHEN** a code source's symbol is a long test method name
- **THEN** its row is cut with an ellipsis inside the answer, the conversation gets no horizontal scroll, and the full place shows on hover

#### Scenario: Many sources
- **WHEN** an answer has 13 sources
- **THEN** it lists five and a "Show all 13" control

#### Scenario: From source to snippet
- **WHEN** the user chooses `src/Maf.Lab.Api/Agent/ToolSource.cs:17-27` in Sources
- **THEN** the right pane shows Code snippets with that snippet highlighted and scrolled into view

### Requirement: The main navigation links to the inspectors
The main navigation SHALL end, after "Curriculum", with three links: "A2A Inspector", "MCP Inspector" and "Redis
Insight", to ports 7172, 7173 and 7174 on the host the page was loaded from (so they work whether the lab is opened as
`localhost` or `127.0.0.1`). Each SHALL open in a new tab without giving the opened page access to the lab's window, and
SHALL be marked as leading outside the app (a visible external-link sign and an accessible name that says it opens in
a new tab). They SHALL be visible to every signed-in user, styled like the other navigation links in both themes, and
SHALL NOT carry any token or other state in the URL.

#### Scenario: Links present
- **WHEN** the app is open at `http://localhost:7171`
- **THEN** after "Curriculum" the navigation shows "A2A Inspector", "MCP Inspector" and "Redis Insight", pointing at
  `http://localhost:7172`, `http://localhost:7173` and `http://localhost:7174`

#### Scenario: Opened from 127.0.0.1
- **WHEN** the app is open at `http://127.0.0.1:7171`
- **THEN** the links point at `http://127.0.0.1:7172`, `:7173` and `:7174`

#### Scenario: New tab, no opener
- **WHEN** a user clicks "Redis Insight"
- **THEN** it opens in a new tab with `rel="noopener noreferrer"`, and the lab's tab stays where it was

### Requirement: The header stays in view while the page scrolls
The app's header (brand, main navigation, persona picker and theme button) SHALL stay at the top of the window while
the page scrolls vertically, on every screen and in both themes. It SHALL keep an opaque background and its bottom
border, so page content scrolling under it is not visible through it. It SHALL wrap to as many rows as the window
width needs, and the behaviour SHALL follow its real height at any width, without assuming a fixed height.

It SHALL show above page content (tooltips, menus, sticky table columns) and below the app's overlays (dialogs, the
chat history drawer and its backdrop). In-page jumps (a link to an `#anchor`, such as the Curriculum section links,
and an element scrolled into view) SHALL bring their target to rest below the header, not under it. A screen that sizes
itself to the window SHALL fit below the header without making the window scroll as well.

When the header would take more than a third of the window's height, it SHALL NOT stay in view and SHALL scroll with
the page, so it does not take most of a small screen. It SHALL NOT stay in view when the page is printed.

#### Scenario: Long page
- **WHEN** a user scrolls down the Curriculum screen in a 1440×900 window
- **THEN** the header with the main navigation, the persona picker and the theme button stays at the top of the window,
  and the cards scroll under it without showing through

#### Scenario: Section link lands below the header
- **WHEN** a user clicks "Not covered" in the Curriculum section links
- **THEN** the page scrolls so the "Not covered" heading is fully visible just below the header

#### Scenario: Wrapped header
- **WHEN** the window is 800 pixels wide and 900 tall, so the header wraps to several rows
- **THEN** the whole wrapped header stays at the top while the page scrolls, and section links land below all its rows

#### Scenario: Both themes
- **WHEN** the theme is switched between Light and Dark while the page is scrolled
- **THEN** the header stays in view with the theme's surface colour and border

#### Scenario: Small window
- **WHEN** the window is 375×667, where the wrapped header is taller than a third of the window
- **THEN** the header scrolls away with the page, and section links land at the top of the window

#### Scenario: Overlays cover the header
- **WHEN** the chat history drawer or a dialog is open
- **THEN** it and its backdrop show above the header

#### Scenario: Chat fits below the header
- **WHEN** the chat screen is open in a 1440×900 window
- **THEN** its panes fit between the header and the bottom of the window, and the window itself does not scroll
