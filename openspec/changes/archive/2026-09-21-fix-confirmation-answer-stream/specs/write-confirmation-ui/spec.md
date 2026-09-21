# Spec Delta

## MODIFIED Requirements

### Requirement: Approving and rejecting are the person's two answers
The card SHALL offer exactly two answers. Approving SHALL resume the proposal as approved and rejecting SHALL
resume it as declined; both SHALL report the outcome in the conversation, and the card SHALL then show what
became of the proposal rather than continuing to ask. While an answer is in flight neither answer SHALL be
accepted a second time. The resume run that carries the answer SHALL stream through the same live event
pipeline as any other run: the Behind-the-scenes panel SHALL remain visible and follow the answer turn while
it streams, exactly as it does for a question-answering turn.

#### Scenario: Approving
- **WHEN** the person approves
- **THEN** the proposal is resumed as approved, the conversation says what was applied, and the card no longer offers to answer

#### Scenario: Rejecting
- **WHEN** the person rejects
- **THEN** the proposal is resumed as declined, the conversation says nothing was applied, and the card no longer offers to answer

#### Scenario: One answer at a time
- **WHEN** an answer is in flight
- **THEN** the buttons cannot be pressed again

#### Scenario: A proposal that is no longer waiting
- **WHEN** the answer reports that the proposal is no longer waiting
- **THEN** the card says so and offers no further answer

#### Scenario: Monitor panel during answer
- **WHEN** the person clicks Approve or Reject and the resume run is in flight
- **THEN** the Behind-the-scenes panel stays visible and displays the live events from the answer run as they arrive, and it continues to show the completed trace once the run ends
