# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Chat as of a step
When the cursor of the selected turn is before its last step, the chat pane SHALL show that turn as it was at the
cursor:
- the answer text reconstructed from the `answer.delta` events up to the cursor;
- the reasoning reconstructed from the `reasoning.delta` events up to the cursor, open while the cursor is still
  among them and closed once the answer has started;
- tool cards in their running or finished state;
- sources only once the `sources` event is reached;
- a banner "viewing step k of N" with a control to return to the present.

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
