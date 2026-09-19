# Spec Delta

## ADDED Requirements

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
