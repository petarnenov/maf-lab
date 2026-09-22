# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
