# Spec Delta

## ADDED Requirements

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
