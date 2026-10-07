## MODIFIED Requirements

### Requirement: A waiting write is shown in the conversation
When a run pauses for an approval, the conversation SHALL show what is waiting, in place, as part of the turn
that proposed it — not as a dialog that covers the conversation. It SHALL show the proposal's summary as the write's
plugin describes it — through the plugin's renderer for that tool when it has one, otherwise from the summary's schema,
each field by its title in the schema's order — and the question in the words the server asked it. For a fee
adjustment that is the account by id and name, the fee as it stands, the amount, the fee that would result and the
period affected.

#### Scenario: A proposal appears
- **WHEN** a run pauses for an approval
- **THEN** the turn shows the proposal's summary and the question, with a way to approve and a way to reject

#### Scenario: A fee adjustment appears
- **WHEN** a run pauses for a fee adjustment
- **THEN** the turn shows the account, the current fee, the amount, the resulting fee and the period

#### Scenario: A write with no renderer
- **WHEN** a run pauses for a write whose plugin has no web renderer
- **THEN** the turn shows each field of its summary by the title its schema gives it

#### Scenario: Not a dialog
- **WHEN** a proposal is waiting
- **THEN** the rest of the conversation is still readable and the history is still usable

#### Scenario: Nothing has happened yet
- **WHEN** a proposal is waiting and nobody has answered
- **THEN** nothing says the write was applied

### Requirement: A proposal outlives the page
A proposal that is still waiting SHALL be findable from the conversation it belongs to, so that opening that
conversation again shows the same card. What is shown SHALL be the proposal as the server has it — its tool, summary
and question — and only the person it was put to SHALL be able to see or answer it.

#### Scenario: Coming back to it
- **WHEN** a conversation with a waiting proposal is opened again
- **THEN** the same proposal is shown, with the same summary and question, still answerable

#### Scenario: Answered in the meantime
- **WHEN** the proposal was answered elsewhere before the page was opened
- **THEN** no card is shown

#### Scenario: Somebody else's conversation
- **WHEN** another user asks what a conversation is waiting on
- **THEN** they are told there is no such conversation
