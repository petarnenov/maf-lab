# Spec Delta

## ADDED Requirements

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
