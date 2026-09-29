# Spec Delta

## ADDED Requirements

### Requirement: A turn's data cards are kept with it
The data cards a turn emitted SHALL be stored with the turn, in the order they arrived, with their type and content.
Opening a stored conversation SHALL show each turn's cards as they were shown live.

A turn stored before cards existed SHALL open with no cards and no error.

Cards SHALL be kept and deleted with their turn: they follow the conversation's retention and deletion.

#### Scenario: Reopening a conversation with a card
- **WHEN** a user reopens a conversation in which a turn showed a holdings card
- **THEN** that turn shows the same holdings card

#### Scenario: An older turn
- **WHEN** a conversation stored before this change is opened
- **THEN** its turns open as before, with no cards
