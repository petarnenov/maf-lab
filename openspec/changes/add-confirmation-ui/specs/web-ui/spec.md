# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
