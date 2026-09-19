# Spec Delta

## MODIFIED Requirements

### Requirement: History in the chat screen
The `/chat` screen SHALL show a history sidebar to the left of the conversation, collapsible on wide screens and shown
as a drawer on screens narrower than 1024 px. The sidebar SHALL provide "New conversation", search, the conversation
list with the active one highlighted, and rename and delete actions. The active conversation SHALL be addressed by the
URL `/chat/{conversationId}`, so reloading reopens it. Selecting a restored turn SHALL show its stored trace in the
monitor with time travel, or "trace expired" when it has been deleted by retention.

Starting a new conversation SHALL be final: once the user asks for one, nothing the screen already holds about the
previous conversation — neither its stored turns nor any cached copy of them — SHALL put it back on screen or back
into the URL. A conversation's id SHALL enter the URL only when that conversation was created by sending a message.

#### Scenario: Reload keeps the conversation
- **WHEN** the user reloads the page while on `/chat/{conversationId}`
- **THEN** the conversation's turns are shown again and the monitor can open each turn's trace

#### Scenario: Switch conversations
- **WHEN** the user selects another conversation in the sidebar
- **THEN** the URL changes to that conversation and its turns replace the previous ones

#### Scenario: New conversation
- **WHEN** the user clicks "New conversation"
- **THEN** the URL becomes `/chat`, the chat is empty, and the next message creates a new conversation that appears at the top of the list

#### Scenario: New conversation while a stored one is open
- **WHEN** the user opens a stored conversation and then clicks "New conversation"
- **THEN** the URL stays `/chat` and the chat stays empty, and the previous conversation's turns do not reappear
