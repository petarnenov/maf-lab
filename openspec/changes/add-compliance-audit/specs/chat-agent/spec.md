# Spec Delta

## MODIFIED Requirements

### Requirement: Tool audit log
Every tool invocation, including attempts to call non-existent tools, SHALL be
recorded with principal id, tool name, argument identifiers (never full text),
outcome, and duration. Logs MUST NOT contain message content.

The same record SHALL also carry actions that are not tool calls, each marked with its kind, so that tool use,
deletion of data and extraction of data are one ordered record rather than three.

#### Scenario: Audit entry
- **WHEN** the agent calls `search_documents`
- **THEN** an audit entry exists with the principal id, tool name, outcome and duration, and without the query text

#### Scenario: Kinds share one record
- **WHEN** a turn calls a tool and the user later deletes that conversation
- **THEN** both appear in the same record, distinguishable by kind
