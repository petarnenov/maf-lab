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
