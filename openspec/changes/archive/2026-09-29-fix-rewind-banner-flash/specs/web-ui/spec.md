# Spec Delta

## MODIFIED Requirements

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
