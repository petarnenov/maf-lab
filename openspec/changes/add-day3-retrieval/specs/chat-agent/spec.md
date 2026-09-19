# Spec Delta

## Purpose

Defines the assistant users talk to: how it decides to use tools, how its
actions are audited, how responses stream to the client, and how
conversations are remembered per principal.

## ADDED Requirements

### Requirement: Retrieval only through the tool
The agent SHALL obtain documentation content only by calling
`search_documents` through the MCP server. It MUST NOT perform retrieval
outside a tool call.

#### Scenario: Greeting needs no retrieval
- **WHEN** the user says "thanks, that's all"
- **THEN** the agent answers without calling any tool and no retrieval occurs

### Requirement: Conditional forced retrieval
Before the first model call of a turn, the system SHALL classify the user's
intent. When the question is procedural, the agent SHALL be required to call
`search_documents` for that turn only. Tool use SHALL never be forced globally.

#### Scenario: Procedural question
- **WHEN** the user asks "what is the procedure when a fee schedule is missing"
- **THEN** `search_documents` is called in that turn

#### Scenario: Next turn is not forced
- **WHEN** the following turn is "status of run 4417"
- **THEN** the agent is free to call `get_billing_run_status` without first calling `search_documents`

### Requirement: Tool audit log
Every tool invocation, including attempts to call non-existent tools, SHALL be
recorded with principal id, tool name, argument identifiers (never full text),
outcome, and duration. Logs MUST NOT contain message content.

#### Scenario: Audit entry
- **WHEN** the agent calls `search_documents`
- **THEN** an audit entry exists with the principal id, tool name, outcome and duration, and without the query text

### Requirement: SSE event stream
The chat endpoint SHALL stream a turn as server-sent events of types
`text_delta`, `tool_call_started` (tool name and argument summary),
`tool_call_finished` (result summary and source count), `sources` (docId,
sectionPath, snippet), and `done`. `tool_call_started` SHALL be emitted before
the tool executes and `sources` before `done`.

#### Scenario: Event ordering
- **WHEN** a turn calls `search_documents`
- **THEN** the client receives `tool_call_started` before the tool executes, then `tool_call_finished`, then `sources`, and `done` last

### Requirement: Persisted conversation memory
Conversations SHALL be identified by a server-issued conversation id bound to
the principal, persisted outside the API process, and limited to a token
window when sent to the model.

#### Scenario: Restart survives
- **WHEN** the API restarts between two turns of a conversation
- **THEN** the second turn has the prior turns in context

#### Scenario: Foreign conversation id
- **WHEN** a user sends a conversation id issued to a different principal
- **THEN** the request is rejected as not found
