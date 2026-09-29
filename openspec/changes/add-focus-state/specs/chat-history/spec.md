# Spec Delta

## ADDED Requirements

### Requirement: The account in focus is kept with the conversation
A conversation's account in focus SHALL be stored with the conversation. The conversation detail SHALL return it as
`focus: { accountId } | null`. A conversation stored before focus existed SHALL return `null`.

#### Scenario: Reopening
- **WHEN** a user reopens a conversation whose last read was A-1043's portfolio
- **THEN** the conversation detail carries `focus: { accountId: "A-1043" }`

#### Scenario: An older conversation
- **WHEN** a conversation stored before this change is opened
- **THEN** its detail carries `focus: null`
