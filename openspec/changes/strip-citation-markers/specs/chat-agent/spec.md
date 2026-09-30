## ADDED Requirements

### Requirement: Answers carry no provider citation markers
The model's answer text SHALL reach the user without inline citation markers of the form `【…】` (U+3010 … U+3011),
whatever the marker contains. This SHALL hold for every place the answer goes: the streamed deltas, the stored turn,
the conversation history offered to later turns, the answer check, and the reply to an A2A caller. The whitespace
directly before a removed marker SHALL be removed with it. The rest of the answer text SHALL be unchanged. The
model's reasoning text is out of scope.

A marker split across streamed deltas SHALL still be removed. An opening `【` that is not closed within 400
characters SHALL be passed through as ordinary text, together with what followed it, so that no answer text is lost.

The turn's trace SHALL record, on `turn.end`, how many markers were removed (`citationMarkersRemoved`, 0 when none).

#### Scenario: A marker at the end of a sentence
- **WHEN** the model answers `the failure code is FS-REQUIRED 【tool_data†get_billing_run_status】.`
- **THEN** the user sees `the failure code is FS-REQUIRED.`
- **AND** `turn.end` records `citationMarkersRemoved` 1

#### Scenario: A marker split across deltas
- **WHEN** the model streams `assign a schedule 【sourcePath: procedures/`, then `missing-fee-schedule.txt】 and re-run`
- **THEN** the streamed text the client receives is `assign a schedule and re-run`

#### Scenario: A stray opening bracket
- **WHEN** the answer contains `【` and no `】` within the next 400 characters
- **THEN** the bracket and the text after it are delivered unchanged

#### Scenario: An answer without markers
- **WHEN** the model's answer contains no `【`
- **THEN** the answer is delivered unchanged and `citationMarkersRemoved` is 0

#### Scenario: A2A reply
- **WHEN** an A2A caller asks a question and the model's answer contains markers
- **THEN** the message returned to the caller contains none
