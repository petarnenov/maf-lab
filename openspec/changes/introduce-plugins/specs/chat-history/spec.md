# Spec Delta

## MODIFIED Requirements

### Requirement: Open a conversation
Opening a conversation SHALL return all of its turns in order. Each turn SHALL include its question, answer, creation
time, tool calls (tool, argument summary, outcome, result summary, source count), sources (doc id, section, source path,
snippet), the feedback kinds the user already gave, and the model's reasoning with how long it took (none for a model
that did not reason). A conversation that does
not belong to the caller, or was deleted, MUST return not found.

#### Scenario: Restore a turn
- **WHEN** Adam opens a conversation whose turn called search_documents and got 5 sources
- **THEN** the turn shows its answer, a finished tool card with its result summary, the 5 sources with snippets, and its feedback state

#### Scenario: Someone else's conversation
- **WHEN** Rita opens Adam's conversation id
- **THEN** the response is not found

#### Scenario: Older turns
- **WHEN** a turn was stored before sources and tool summaries were persisted in full
- **THEN** it still opens, showing the fields it has

### Requirement: Conversation list
While the `conversation-history` plugin is installed, the system SHALL list the caller's own, non-deleted conversations
ordered by last activity (newest first). Each entry SHALL include the conversation id, title, creation time, last
activity time and turn count. The list SHALL support a case-insensitive search over titles, questions and answers, and
paging with a limit (default 30, maximum 100) and a cursor. Conversations of other users, including other users of the
same tenant, MUST NOT be listed. Without the plugin, the list route SHALL not exist.

#### Scenario: Own conversations only
- **WHEN** Adam of tenant A lists conversations after Rita (tenant A) and Bianca (tenant B) have chatted
- **THEN** only Adam's conversations are returned

#### Scenario: Search
- **WHEN** Adam searches for "credit"
- **THEN** only conversations whose title, question or answer contains "credit" are returned

#### Scenario: Empty conversations are hidden
- **WHEN** a conversation was created but has no turns
- **THEN** it is not listed

### Requirement: Titles
A conversation's title SHALL default to its first question, truncated to 80 characters at a word boundary, and SHALL be
shown on the opened conversation (core). While the `conversation-history` plugin is installed, the owner SHALL be able
to rename it to 1–120 non-blank characters.

#### Scenario: Default title
- **WHEN** the first question of a conversation is "How do I issue a billing credit to a client who was overcharged?"
- **THEN** the listed title is that question (truncated if longer than 80 characters)

#### Scenario: Rename
- **WHEN** the owner renames the conversation to "Credits for Smith household"
- **THEN** the list and the opened conversation show the new title

### Requirement: Delete a conversation
While the `conversation-history` plugin is installed, the owner SHALL be able to delete a conversation after
confirming. A deleted conversation MUST disappear from the list, MUST return not found when opened, and MUST reject new
messages. Its turns, feedback and traces SHALL remain available to the tenant's review queue and retention rules. The
soft delete SHALL go through the core's conversation store, which records it in the audit.

#### Scenario: Delete
- **WHEN** Adam deletes a conversation and confirms
- **THEN** it is no longer listed, opening it returns not found, and posting a message to it returns not found

#### Scenario: Review queue unaffected
- **WHEN** a deleted conversation had a flagged turn
- **THEN** that turn is still in the TENANT_ADMIN review queue

### Requirement: History in the chat screen
The `/chat` screen SHALL always offer "New conversation" in the chat's own header (core). The active conversation SHALL
be addressed by the URL `/chat/{conversationId}`, so reloading reopens it. While the `conversation-history` plugin is
installed, the screen SHALL show its history sidebar to the left of the conversation, collapsible on wide screens and
shown as a drawer on screens narrower than 1024 px, with search, the conversation list with the active one highlighted,
and rename and delete actions; without the plugin, no sidebar, drawer, toggle or rail SHALL be shown. Selecting a
restored turn SHALL show its stored trace in the monitor with time travel, or "trace expired" when it has been deleted
by retention, while the `monitor` plugin is installed.

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
