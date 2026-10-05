# Spec Delta

## MODIFIED Requirements

### Requirement: Conversation list
The system SHALL list the caller's own, non-deleted conversations ordered by last activity (newest first). Each entry
SHALL include the conversation id, title, creation time, last activity time and turn count. The list SHALL support a
case-insensitive search over titles, questions and answers, and paging with a limit (default 30, maximum 100) and a
cursor. Conversations of other users, including other users of the same tenant, MUST NOT be listed.

#### Scenario: Own conversations only
- **WHEN** Adam of tenant A lists conversations after Rita (tenant A) and Bianca (tenant B) have chatted
- **THEN** only Adam's conversations are returned

#### Scenario: Search
- **WHEN** Adam searches for "credit"
- **THEN** only conversations whose title, question or answer contains "credit" are returned

#### Scenario: Empty conversations are hidden
- **WHEN** a conversation was created but has no turns
- **THEN** it is not listed

### Requirement: Delete a conversation
The owner SHALL be able to delete a conversation after confirming. A deleted conversation MUST disappear from the list,
MUST return not found when opened, and MUST reject new messages. Its turns, feedback and traces SHALL remain available
to the tenant's review queue and retention rules.

#### Scenario: Delete
- **WHEN** Adam deletes a conversation and confirms
- **THEN** it is no longer listed, opening it returns not found, and posting a message to it returns not found

#### Scenario: Review queue unaffected
- **WHEN** a deleted conversation had a flagged turn
- **THEN** that turn is still in the TENANT_ADMIN review queue
