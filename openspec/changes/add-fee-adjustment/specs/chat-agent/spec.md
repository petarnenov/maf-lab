# Spec Delta

## MODIFIED Requirements

### Requirement: SSE event stream
The chat endpoint SHALL stream a turn as server-sent events of types
`text_delta`, `tool_call_started` (tool name and argument summary),
`tool_call_finished` (result summary and source count), `sources` (docId,
sectionPath, snippet), `trace` (one turn-trace event: sequence number, elapsed
milliseconds, kind, title, optional duration and structured data),
`confirmation_required` (a summary a person can check and the opaque state that
identifies what would be executed), and `done`. `tool_call_started` SHALL be
emitted before the tool executes and `sources` before `done`. `trace` events MAY
interleave with every other event and MUST all arrive before `done`.
`confirmation_required` SHALL be emitted at most once per turn and SHALL be
followed by `done`, because the turn ends waiting for a person.

#### Scenario: Event ordering
- **WHEN** a turn calls `search_documents`
- **THEN** the client receives `tool_call_started` before the tool executes, then `tool_call_finished`, then `sources`, and `done` last

#### Scenario: Trace events in the stream
- **WHEN** any turn runs
- **THEN** the stream contains `trace` events with strictly increasing sequence numbers, the first of kind `turn.start` and the last before `done` of kind `turn.end`

#### Scenario: A turn that ends waiting
- **WHEN** a turn proposes an adjustment that needs confirming
- **THEN** the stream carries one `confirmation_required` with the summary and the state, then `done`, and no further tool executes in that turn

### Requirement: Tool audit log
Every tool invocation, including attempts to call non-existent tools, SHALL be
recorded with principal id, tool name, argument identifiers (never full text),
outcome, and duration. Logs MUST NOT contain message content. Arguments that are
free text — a query, a question, a reason, a justification — SHALL be reduced to
the fact that they were given, never their content.

The same record SHALL also carry actions that are not tool calls, each marked with its kind, so that tool use,
deletion of data and extraction of data are one ordered record rather than three.

#### Scenario: Audit entry
- **WHEN** the agent calls `search_documents`
- **THEN** an audit entry exists with the principal id, tool name, outcome and duration, and without the query text

#### Scenario: Kinds share one record
- **WHEN** a turn calls a tool and the user later deletes that conversation
- **THEN** both appear in the same record, distinguishable by kind

#### Scenario: Unknown tool recorded
- **WHEN** the model emits a call to a tool that does not exist
- **THEN** the attempt is recorded with its outcome and the call does not execute

#### Scenario: A write tool's reason is not logged
- **WHEN** an adjustment is proposed with a reason
- **THEN** the record names the account and the adjustment but not the reason's text
